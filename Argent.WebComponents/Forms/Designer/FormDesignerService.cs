using Argent.Core.Forms;
using Argent.Core.Forms.Protocol.V2;
using Argent.Core.DomainObjects;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Argent.WebComponents.Forms.Designer;

public class ToolboxItem
{
    public required string TypeName { get; init; }
    public required string DisplayName { get; init; }
    public required string Icon { get; init; }
    public required string Category { get; init; }
    public required Func<FormComponent> Factory { get; init; }
}

public readonly record struct DropTarget(string? ContainerId, int Index, int ColumnIndex = 0);

public class FormDesignerService(IFormDesignerStore _store)
{
    public FormDefinition Definition { get; private set; } = NewDefinition();
    public string? SelectedBindingKey { get; private set; }
    public FormObjectBinding? SelectedBinding => Definition.Objects.FirstOrDefault(binding => binding.Key == SelectedBindingKey);

    public void SelectBinding(string key)
    {
        SelectedBindingKey = key;
        SelectComponent(null);
        Notify();
    }

    public FormObjectBinding AddObject(string objectKey)
    {
        var key = objectKey;
        for (var suffix = 2; Definition.Objects.Any(binding => binding.Key == key); suffix++)
            key = $"{objectKey}{suffix}";
        var binding = new FormObjectBinding
        {
            Key = key,
            ObjectKey = objectKey,
            IsPrimary = Definition.Objects.Count == 0
        };
        Definition.Objects.Add(binding);
        if (binding.IsPrimary) Definition.ObjectKey = objectKey;
        SelectedBindingKey = key;
        SelectedComponent = null;
        MarkDirty();
        return binding;
    }

    public void RemoveObject(FormObjectBinding binding)
    {
        Definition.Objects.Remove(binding);
        foreach (var field in AllFields().Where(field => field.ObjectBinding == binding.Key))
        {
            field.ObjectBinding = null;
            field.PropertyKey = null;
        }
        if (binding.IsPrimary && Definition.Objects.Count > 0)
        {
            Definition.Objects[0].IsPrimary = true;
            Definition.Objects[0].When = null;
            Definition.ObjectKey = Definition.Objects[0].ObjectKey;
        }
        else if (Definition.Objects.Count == 0) Definition.ObjectKey = string.Empty;
        SelectedBindingKey = Definition.Objects.FirstOrDefault()?.Key;
        MarkDirty();
    }

    public void SetPrimary(FormObjectBinding binding)
    {
        foreach (var item in Definition.Objects) item.IsPrimary = item == binding;
        binding.When = null;
        Definition.ObjectKey = binding.ObjectKey;
        MarkDirty();
    }

    public FormComponent? SelectedComponent { get; private set; }
    public Guid? StoredFormId { get; set; }
    public string Name { get; set; } = "New Form";
    public string Description { get; set; } = "";
    public bool HasUnsavedChanges { get; private set; }

    public bool IsDragging { get; private set; }
    private string? _dragPayload;
    public DropTarget? Hover { get; private set; }

    public event Action? OnChange;
    public void Notify() => OnChange?.Invoke();
    public void MarkDirty() { HasUnsavedChanges = true; Notify(); }

    public List<FormDesignVersion> Versions { get; private set; } = [];
    public bool IsReadOnly { get; private set; }
    public bool HasDraft { get; private set; }

    /// <summary>Set by the hosting component on initialization from AuthenticationStateProvider.</summary>
    public string UserName { get; set; } = "Unknown";
    /// <summary>Set by the hosting component — ASP.NET Identity user ID (GUID string), used for ownership on new forms.</summary>
    public string? UserIdentityId { get; set; }

    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static FormDefinition NewDefinition() => new()
    {
        ProtocolVersion = "2.0",
        Id = $"form-{Guid.NewGuid():N}",
        Components = []
    };

    public static readonly List<ToolboxItem> ToolboxItems =
    [
        Field("text", "Text", "bi-input-cursor-text"),
        Field("integer", "Integer", "bi-123"),
        Field("decimal", "Decimal", "bi-hash"),
        Field("date", "Date", "bi-calendar3"),
        Field("timestamp", "Timestamp", "bi-clock"),
        Field("boolean", "Checkbox", "bi-check-square"),
        Field("choice", "Choice", "bi-menu-down", () => new FormField { Type = "choice", Label = "Choice", Options = [new() { Label = "Option 1", Value = "opt1" }, new() { Label = "Option 2", Value = "opt2" }] }),
        Field("reference", "Reference", "bi-link-45deg", () => new FormField
        {
            Type = "choice",
            Label = "Reference",
            Reference = new FormReferenceSource()
        }),
        // Attachment storage and upload are intentionally deferred; do not advertise a control
        // that cannot yet produce a durable attachment reference.
        Layout("section", "Section", "bi-border-all"),
        Layout("row", "Row", "bi-distribute-horizontal"),
        Layout("column", "Column", "bi-layout-split"),
        Layout("tabs", "Tabs", "bi-files"),
        Layout("accordion", "Accordion", "bi-arrows-collapse")
    ];

    private static ToolboxItem Field(string type, string label, string icon, Func<FormComponent>? factory = null) => new()
    {
        TypeName = type, Category = "Fields", DisplayName = label, Icon = icon,
        Factory = factory ?? (() => new FormField { Type = type, Label = label })
    };

    private static ToolboxItem Layout(string type, string label, string icon) => new()
    {
        TypeName = type, Category = "Layout", DisplayName = label, Icon = icon,
        Factory = () => new FormLayout { Type = type, Id = $"layout-{Guid.NewGuid():N}", Title = type == "section" ? label : null }
    };

    public void SelectComponent(FormComponent? component)
    {
        if (SelectedComponent == component) return;
        SelectedComponent = component;
        Notify();
    }

    public FormComponent? Find(string id) => Find(id, Definition.Components);

    private static FormComponent? Find(string id, List<FormComponent> list)
    {
        foreach (var c in list)
        {
            if (c.EditorId == id) return c;
            if (c is FormLayout l && Find(id, l.Children) is { } found)
                return found;
        }
        return null;
    }

    private (List<FormComponent> list, int index)? FindParentList(string id) =>
        FindParentList(id, Definition.Components);

    private static (List<FormComponent> list, int index)? FindParentList(string id, List<FormComponent> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].EditorId == id) return (list, i);
            if (list[i] is FormLayout l && FindParentList(id, l.Children) is { } found)
                return found;
        }
        return null;
    }

    private List<FormComponent>? ResolveContainerItems(string? containerId)
    {
        if (containerId == null) return Definition.Components;
        return (Find(containerId) as FormLayout)?.Children;
    }

    public void Remove(FormComponent component)
    {
        var location = FindParentList(component.EditorId);
        if (location == null) return;
        location.Value.list.RemoveAt(location.Value.index);
        if (SelectedComponent != null && (SelectedComponent == component || IsDescendantOf(SelectedComponent.EditorId, component)))
            SelectedComponent = null;
        MarkDirty();
    }

    public void RemoveSelected()
    {
        if (SelectedComponent != null)
            Remove(SelectedComponent);
    }

    public void Duplicate(FormComponent component)
    {
        var location = FindParentList(component.EditorId);
        if (location == null) return;
        var clone = CloneComponent(component);
        if (clone == null) return;
        RegenerateIdsAndNames(clone);
        location.Value.list.Insert(location.Value.index + 1, clone);
        SelectedComponent = clone;
        MarkDirty();
    }

    private FormComponent? CloneComponent(FormComponent component)
    {
        var json = JsonSerializer.Serialize(component, CloneOptions);
        return JsonSerializer.Deserialize<FormComponent>(json, CloneOptions);
    }

    private void RegenerateIdsAndNames(FormComponent component)
    {
        component.EditorId = Guid.NewGuid().ToString("N");
        if (component is FormField field && !string.IsNullOrEmpty(field.Name))
            field.Name = UniqueFieldName(field.Name);
        if (component is FormLayout layout)
        {
            layout.Id = $"layout-{Guid.NewGuid():N}";
            foreach (var child in layout.Children)
                RegenerateIdsAndNames(child);
        }
    }

    public void RenameField(FormField field, string newName)
    {
        newName = newName.Trim();
        if (string.Equals(field.Name, newName, StringComparison.Ordinal)) return;

        var oldName = field.Name;
        field.Name = newName;
        WalkComponents(Definition.Components, component =>
        {
            RewriteExpression(component is FormField candidate ? candidate.VisibleWhen : ((FormLayout)component).VisibleWhen, oldName, newName);
            if (component is not FormField formField) return;
            RewriteExpression(formField.RequiredWhen, oldName, newName);
            RewriteExpression(formField.DisabledWhen, oldName, newName);
            RewriteExpression(formField.ReadOnlyWhen, oldName, newName);
            foreach (var validator in formField.Validators)
            {
                RewriteExpression(validator.When, oldName, newName);
                if (string.Equals(validator.OtherField, oldName, StringComparison.Ordinal))
                    validator.OtherField = newName;
            }
        });
        MarkDirty();
    }

    private static void WalkComponents(IEnumerable<FormComponent> components, Action<FormComponent> visitor)
    {
        foreach (var component in components)
        {
            visitor(component);
            if (component is FormLayout layout) WalkComponents(layout.Children, visitor);
        }
    }

    private static void RewriteExpression(FormExpression? expression, string oldName, string newName)
    {
        if (expression is null) return;
        RewriteOperand(expression.Left, oldName, newName);
        RewriteOperand(expression.Right, oldName, newName);
        RewriteOperand(expression.Operand, oldName, newName);
        if (expression.Argument is not null) RewriteExpression(expression.Argument, oldName, newName);
        if (expression.Arguments is not null)
            foreach (var argument in expression.Arguments) RewriteExpression(argument, oldName, newName);
    }

    private static void RewriteOperand(FormOperand? operand, string oldName, string newName)
    {
        if (operand is not null && string.Equals(operand.Field, oldName, StringComparison.Ordinal))
            operand.Field = newName;
    }

    public string UniqueFieldName(string baseName)
    {
        var existing = AllFieldNames();
        if (!existing.Contains(baseName)) return baseName;
        var stem = System.Text.RegularExpressions.Regex.Replace(baseName, @"_\d+$", "");
        for (int i = 2; ; i++)
        {
            var candidate = $"{stem}_{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    public HashSet<string> AllFieldNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(List<FormComponent> list)
        {
            foreach (var c in list)
            {
                if (c is FormField f && !string.IsNullOrEmpty(f.Name)) names.Add(f.Name);
                if (c is FormLayout l) Walk(l.Children);
            }
        }
        Walk(Definition.Components);
        return names;
    }

    public List<string> FieldNames() => [.. AllFieldNames().Order()];

    public IEnumerable<FormField> AllFields()
    {
        var fields = new List<FormField>();
        WalkComponents(Definition.Components, component =>
        {
            if (component is FormField field) fields.Add(field);
        });
        return fields;
    }

    public static string FieldTypeFor(DomainPropertyType type) => type switch
    {
        DomainPropertyType.Number => "decimal",
        DomainPropertyType.Boolean => "boolean",
        DomainPropertyType.Date => "date",
        DomainPropertyType.DateTime => "timestamp",
        DomainPropertyType.Choice or DomainPropertyType.Reference => "choice",
        _ => "text"
    };

    private static bool IsDescendantOf(string id, FormComponent ancestor) =>
        ancestor is FormLayout layout && Find(id, layout.Children) != null;

    public void BeginDragAdd(string xtype) => BeginDrag("add:" + xtype);
    public void BeginDragMove(string componentId) => BeginDrag("move:" + componentId);

    private void BeginDrag(string payload)
    {
        IsDragging = true;
        _dragPayload = payload;
        Hover = null;
        Notify();
    }

    public void EndDrag()
    {
        IsDragging = false;
        _dragPayload = null;
        Hover = null;
        Notify();
    }

    public void SetHover(DropTarget target)
    {
        if (Hover == target) return;
        if (!CanDrop(target)) { Hover = null; Notify(); return; }
        Hover = target;
        Notify();
    }

    public void ClearHover(DropTarget target)
    {
        if (Hover == target) { Hover = null; Notify(); }
    }

    public bool CanDrop(DropTarget target)
    {
        if (_dragPayload == null) return false;
        if (!_dragPayload.StartsWith("move:")) return true;
        var id = _dragPayload["move:".Length..];
        if (target.ContainerId == null) return true;
        if (target.ContainerId == id) return false;
        var dragged = Find(id);
        return dragged == null || !IsDescendantOf(target.ContainerId, dragged);
    }

    public void Drop(DropTarget target)
    {
        if (_dragPayload == null || !CanDrop(target)) { EndDrag(); return; }
        var parts = _dragPayload.Split(':', 2);
        if (parts[0] == "add") DropAdd(parts[1], target);
        else if (parts[0] == "move") DropMove(parts[1], target);
        EndDrag();
    }

    private void DropAdd(string xtype, DropTarget target)
    {
        var component = ToolboxItems.FirstOrDefault(t => t.TypeName == xtype)?.Factory();
        if (component == null) return;
        if (component is FormField field && string.IsNullOrEmpty(field.Name))
            field.Name = UniqueFieldName("field_1");
        InsertAt(component, target);
        SelectedComponent = component;
        MarkDirty();
    }

    private void DropMove(string componentId, DropTarget target)
    {
        var location = FindParentList(componentId);
        if (location == null) return;
        var (sourceList, sourceIndex) = location.Value;
        var component = sourceList[sourceIndex];
        var targetList = ResolveContainerItems(target.ContainerId);
        if (targetList == null) return;
        var index = target.Index;
        sourceList.RemoveAt(sourceIndex);
        if (ReferenceEquals(sourceList, targetList) && sourceIndex < index) index--;
        targetList.Insert(Math.Clamp(index, 0, targetList.Count), component);
        SelectedComponent = component;
        MarkDirty();
    }

    private void InsertAt(FormComponent component, DropTarget target)
    {
        var list = ResolveContainerItems(target.ContainerId);
        if (list == null) return;
        list.Insert(Math.Clamp(target.Index, 0, list.Count), component);
    }

    public void AddFromToolbox(string xtype)
    {
        var container = SelectedComponent as FormLayout ?? FindAncestorLayout(SelectedComponent);
        var items = container?.Children ?? Definition.Components;
        _dragPayload = "add:" + xtype;
        Drop(new DropTarget(container?.EditorId, items.Count));
    }

    public void AddField(FormField field)
    {
        var container = SelectedComponent as FormLayout ?? FindAncestorLayout(SelectedComponent);
        var items = container?.Children ?? Definition.Components;
        items.Add(field);
        SelectedComponent = field;
        MarkDirty();
    }

    private FormLayout? FindAncestorLayout(FormComponent? component)
    {
        if (component == null) return null;
        var location = FindParentList(component.EditorId);
        if (location == null) return null;
        FormLayout? owner = null;
        void Walk(List<FormComponent> list, FormLayout? parent)
        {
            if (ReferenceEquals(list, location.Value.list)) { owner = parent; return; }
            foreach (var c in list)
                if (c is FormLayout l) Walk(l.Children, l);
        }
        Walk(Definition.Components, null);
        return owner;
    }

    public void MoveUp(FormComponent component) => Nudge(component, -1);
    public void MoveDown(FormComponent component) => Nudge(component, +1);

    private void Nudge(FormComponent component, int delta)
    {
        var location = FindParentList(component.EditorId);
        if (location == null) return;
        var (list, index) = location.Value;
        var newIndex = index + delta;
        if (newIndex < 0 || newIndex >= list.Count) return;
        (list[index], list[newIndex]) = (list[newIndex], list[index]);
        MarkDirty();
    }

    public async Task LoadAsync(Guid id)
    {
        var result = await _store.LoadAsync(id);
        if (result == null) return;
        ApplyLoadResult(result);
    }

    public async Task LoadVersionAsync(Guid versionId)
    {
        var result = await _store.LoadVersionAsync(versionId);
        if (result == null) return;
        ApplyLoadResult(result, readOnly: true);
    }

    public async Task CreateDraftFromVersionAsync(Guid versionId)
    {
        var result = await _store.CreateDraftFromVersionAsync(versionId, UserName);
        if (result == null) return;
        ApplyLoadResult(result);
    }

    private void ApplyLoadResult(FormDesignerLoadResult result, bool readOnly = false)
    {
        Definition = result.Definition ?? NewDefinition();
        SelectedBindingKey = Definition.Objects.FirstOrDefault()?.Key;
        Name = result.Name;
        Description = result.Description;
        StoredFormId = result.FormDesignId;
        IsReadOnly = readOnly || result.IsReadOnlyVersion;
        Versions = result.Versions;
        HasDraft = result.DraftId.HasValue;
        SelectedComponent = null;
        HasUnsavedChanges = false;
        Notify();
    }

    public async Task SaveAsync()
    {
        if (IsReadOnly) return;
        EnsureValidDefinition();

        var result = await _store.SaveAsync(new FormDesignerSaveRequest
        {
            FormDesignId = StoredFormId,
            Name = Name,
            Description = Description,
            Definition = CloneDefinition(Definition),
            UserName = UserName,
            UserIdentityId = UserIdentityId
        });

        StoredFormId = result.FormDesignId;
        HasDraft = true;
        HasUnsavedChanges = false;
        Notify();
    }

    public async Task<FormDesignVersion> PublishAsync()
    {
        if (IsReadOnly || !StoredFormId.HasValue)
            throw new InvalidOperationException("Cannot publish: no form design loaded or in read-only mode.");

        EnsureValidDefinition();
        var version = await _store.PublishAsync(new FormPublishRequest
        {
            FormDesignId = StoredFormId.Value,
            UserId = UserName
        });

        Versions.Insert(0, version);
        HasDraft = false;
        HasUnsavedChanges = false;
        Notify();
        return version;
    }

    private FormDefinition CloneDefinition(FormDefinition definition)
    {
        var json = JsonSerializer.Serialize(definition, CloneOptions);
        return JsonSerializer.Deserialize<FormDefinition>(json, CloneOptions) ?? definition;
    }

    private void EnsureValidDefinition()
    {
        var result = FormDefinitionCompiler.Compile(Definition);
        if (!result.IsValid)
            throw new InvalidOperationException("Form Protocol v2 definition is invalid: " +
                string.Join("; ", result.Errors.Select(error => $"{error.Path}: {error.Message}")));
    }

    public void Reset()
    {
        Definition = NewDefinition();
        SelectedBindingKey = null;
        Name = "New Form";
        Description = "";
        StoredFormId = null;
        IsReadOnly = false;
        HasDraft = false;
        Versions = [];
        SelectedComponent = null;
        HasUnsavedChanges = false;
        EndDrag();
    }
}
