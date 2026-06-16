using System.Security.Claims;
using System.Text.Json;
using Argent.Contracts.Authorization;
using Argent.Infrastructure.Data;
using Argent.Infrastructure.Serialization;
using Argent.Models.Forms.Components;
using Argent.Models.Forms.Components.Base;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Forms.Modeling;

public class ToolboxItem
{
    public required string TypeName { get; init; }
    public required string DisplayName { get; init; }
    public required string Icon { get; init; }
    public required string Category { get; init; }
    public required Func<FormComponent> Factory { get; init; }
}

/// <summary>
/// Where a dragged component would land: a parent container (null = form root),
/// an insertion index within that container's items, and — for multi-column
/// containers — the target column.
/// </summary>
public readonly record struct DropTarget(string? ContainerId, int Index, int ColumnIndex = 0);

public class FormDesignerService(
    IDbContextFactory<ArgentDbContext> _dbContextFactory,
    IHttpContextAccessor _httpContextAccessor,
    IResourceOwnershipService _ownershipService)
{
    public FormDefinition Definition { get; private set; } = NewDefinition();

    public FormComponent? SelectedComponent { get; private set; }
    public Guid? StoredFormId { get; set; }
    public string Name { get; set; } = "New Form";
    public string Description { get; set; } = "";
    public bool HasUnsavedChanges { get; private set; }

    // ── Drag state ─────────────────────────────────────────────────
    public bool IsDragging { get; private set; }
    private string? _dragPayload;          // "add:<xtype>" or "move:<componentId>"
    public DropTarget? Hover { get; private set; }

    public event Action? OnChange;
    public void Notify() => OnChange?.Invoke();
    public void MarkDirty() { HasUnsavedChanges = true; Notify(); }

    private static FormDefinition NewDefinition() => new()
    {
        FormId = Guid.NewGuid().ToString(),
        Components = []
    };

    public static readonly List<ToolboxItem> ToolboxItems =
    [
        // Fields
        new() { TypeName = "TextField",     Category = "Fields", DisplayName = "Text",        Icon = "bi-input-cursor-text",     Factory = () => new FormField { Xtype = "TextField",     Name = "", FieldLabel = "Text" } },
        new() { TypeName = "TextArea",      Category = "Fields", DisplayName = "Text Area",   Icon = "bi-textarea-resize",       Factory = () => new FormField { Xtype = "TextField",     Name = "", FieldLabel = "Text Area", Rows = 4, Grow = true } },
        new() { TypeName = "NumericField",  Category = "Fields", DisplayName = "Number",      Icon = "bi-123",                   Factory = () => new FormField { Xtype = "NumericField",  Name = "", FieldLabel = "Number" } },
        new() { TypeName = "DecimalField",  Category = "Fields", DisplayName = "Decimal",     Icon = "bi-hash",                  Factory = () => new FormField { Xtype = "DecimalField",  Name = "", FieldLabel = "Decimal", Precision = 2 } },
        new() { TypeName = "DateField",     Category = "Fields", DisplayName = "Date",        Icon = "bi-calendar3",             Factory = () => new FormField { Xtype = "DateField",     Name = "", FieldLabel = "Date" } },
        new() { TypeName = "SliderField",   Category = "Fields", DisplayName = "Slider",      Icon = "bi-sliders",               Factory = () => new FormField { Xtype = "SliderField",   Name = "", FieldLabel = "Slider", Min = 0, Max = 100, Step = 1 } },
        new() { TypeName = "DropdownField", Category = "Fields", DisplayName = "Dropdown",    Icon = "bi-menu-down",             Factory = () => new FormField { Xtype = "DropdownField", Name = "", FieldLabel = "Dropdown", Items = [new SelectOption { Label = "Option 1", Value = "opt1" }, new SelectOption { Label = "Option 2", Value = "opt2" }] } },
        new() { TypeName = "RadioField",    Category = "Fields", DisplayName = "Radio",       Icon = "bi-record-circle",         Factory = () => new FormField { Xtype = "RadioField",    Name = "", FieldLabel = "Radio",    Items = [new SelectOption { Label = "Option 1", Value = "opt1" }, new SelectOption { Label = "Option 2", Value = "opt2" }] } },
        new() { TypeName = "CheckboxField", Category = "Fields", DisplayName = "Checkbox",    Icon = "bi-check-square",          Factory = () => new FormField { Xtype = "CheckboxField", Name = "", FieldLabel = "Checkbox", Value = false } },
        new() { TypeName = "FileField",     Category = "Fields", DisplayName = "File Upload", Icon = "bi-paperclip",             Factory = () => new FormField { Xtype = "FileField",     Name = "", FieldLabel = "File" } },

        // Layout
        new() { TypeName = "Row",       Category = "Layout", DisplayName = "Row",       Icon = "bi-layout-three-columns", Factory = () => new FormLayout { Xtype = "Row", Direction = "row" } },
        new() { TypeName = "Column",    Category = "Layout", DisplayName = "Column",    Icon = "bi-layout-split",         Factory = () => new FormLayout { Xtype = "Column", Direction = "column" } },
        new() { TypeName = "Flex",      Category = "Layout", DisplayName = "Flex Box",  Icon = "bi-distribute-horizontal",Factory = () => new FormLayout { Xtype = "Flex", Direction = "row", Gap = 3 } },
        new() { TypeName = "Fieldset",  Category = "Layout", DisplayName = "Fieldset",  Icon = "bi-border-all",           Factory = () => new FormLayout { Xtype = "Fieldset", Title = "Fieldset", LayoutType = LayoutType.Fieldset, Direction = "column" } },
        new() { TypeName = "Tabs",      Category = "Layout", DisplayName = "Tabs",      Icon = "bi-files",                Factory = () => new FormLayout { Xtype = "Tabs", LayoutType = LayoutType.Tabs } },
        new() { TypeName = "Accordion", Category = "Layout", DisplayName = "Accordion", Icon = "bi-arrows-collapse",      Factory = () => new FormLayout { Xtype = "Accordion", LayoutType = LayoutType.Accordion } },

        // Content
        new() { TypeName = "HtmlBox", Category = "Content", DisplayName = "HTML Block", Icon = "bi-code-slash", Factory = () => new FormLayout { Xtype = "HtmlBox", Html = "<p>HTML content</p>" } },
    ];

    // ── Selection ──────────────────────────────────────────────────

    public void SelectComponent(FormComponent? component)
    {
        if (SelectedComponent == component) return;
        SelectedComponent = component;
        Notify();
    }

    // ── Tree operations ────────────────────────────────────────────

    public FormComponent? Find(string id) => Find(id, Definition.Components);

    private static FormComponent? Find(string id, List<FormComponent> list)
    {
        foreach (var c in list)
        {
            if (c.Id == id) return c;
            if (c is FormLayout l && Find(id, l.Items) is { } found)
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
            if (list[i].Id == id) return (list, i);
            if (list[i] is FormLayout l && FindParentList(id, l.Items) is { } found)
                return found;
        }
        return null;
    }

    private List<FormComponent>? ResolveContainerItems(string? containerId)
    {
        if (containerId == null) return Definition.Components;
        return (Find(containerId) as FormLayout)?.Items;
    }

    public void Remove(FormComponent component)
    {
        var location = FindParentList(component.Id);
        if (location == null) return;

        location.Value.list.RemoveAt(location.Value.index);
        if (SelectedComponent != null && (SelectedComponent == component || IsDescendantOf(SelectedComponent.Id, component)))
            SelectedComponent = null;
        MarkDirty();
    }

    public void RemoveSelected()
    {
        if (SelectedComponent != null)
            Remove(SelectedComponent);
    }

    /// <summary>Deep-clones a component (new ids, unique field names) next to the original.</summary>
    public void Duplicate(FormComponent component)
    {
        var location = FindParentList(component.Id);
        if (location == null) return;

        var clone = CloneComponent(component);
        if (clone == null) return;

        RegenerateIdsAndNames(clone);
        location.Value.list.Insert(location.Value.index + 1, clone);
        SelectedComponent = clone;
        MarkDirty();
    }

    private static FormComponent? CloneComponent(FormComponent component)
    {
        var json = JsonSerializer.Serialize(component, FormSerializer.Options);
        return JsonSerializer.Deserialize<FormComponent>(json);
    }

    private void RegenerateIdsAndNames(FormComponent component)
    {
        component.Id = Guid.NewGuid().ToString();
        if (component is FormField field && !string.IsNullOrEmpty(field.Name))
            field.Name = UniqueFieldName(field.Name);
        if (component is FormLayout layout)
            foreach (var child in layout.Items)
                RegenerateIdsAndNames(child);
    }

    /// <summary>field → field_2 → field_3 … until the name is unused.</summary>
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
                if (c is FormLayout l) Walk(l.Items);
            }
        }
        Walk(Definition.Components);
        return names;
    }

    public List<string> FieldNames() => [.. AllFieldNames().Order()];

    private static bool IsDescendantOf(string id, FormComponent ancestor) =>
        ancestor is FormLayout layout && Find(id, layout.Items) != null;

    // ── Drag & drop ────────────────────────────────────────────────

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
        // Never highlight a target the payload is not allowed to drop on.
        if (!CanDrop(target)) { Hover = null; Notify(); return; }
        Hover = target;
        Notify();
    }

    public void ClearHover(DropTarget target)
    {
        // dragleave fires after the next dragenter when moving between zones;
        // only clear if we're still the active target.
        if (Hover == target)
        {
            Hover = null;
            Notify();
        }
    }

    public bool CanDrop(DropTarget target)
    {
        if (_dragPayload == null) return false;
        if (!_dragPayload.StartsWith("move:")) return true;

        var id = _dragPayload["move:".Length..];
        // A container must never be dropped into itself or its own subtree.
        if (target.ContainerId == null) return true;
        if (target.ContainerId == id) return false;
        var dragged = Find(id);
        return dragged == null || !IsDescendantOf(target.ContainerId, dragged);
    }

    public void Drop(DropTarget target)
    {
        if (_dragPayload == null || !CanDrop(target)) { EndDrag(); return; }

        var parts = _dragPayload.Split(':', 2);
        if (parts[0] == "add")
            DropAdd(parts[1], target);
        else if (parts[0] == "move")
            DropMove(parts[1], target);

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
        // Removing from the same list above the insertion point shifts it down by one.
        if (ReferenceEquals(sourceList, targetList) && sourceIndex < index)
            index--;

        component.ColumnIndex = Math.Max(0, target.ColumnIndex);
        targetList.Insert(Math.Clamp(index, 0, targetList.Count), component);
        SelectedComponent = component;
        MarkDirty();
    }

    private void InsertAt(FormComponent component, DropTarget target)
    {
        var list = ResolveContainerItems(target.ContainerId);
        if (list == null) return;
        component.ColumnIndex = Math.Max(0, target.ColumnIndex);
        list.Insert(Math.Clamp(target.Index, 0, list.Count), component);
    }

    /// <summary>Click-to-add fallback: appends to the selected container or the root.</summary>
    public void AddFromToolbox(string xtype)
    {
        var container = SelectedComponent as FormLayout ?? FindAncestorLayout(SelectedComponent);
        var items = container?.Items ?? Definition.Components;
        _dragPayload = "add:" + xtype;
        Drop(new DropTarget(container?.Id, items.Count));
    }

    private FormLayout? FindAncestorLayout(FormComponent? component)
    {
        if (component == null) return null;
        var location = FindParentList(component.Id);
        if (location == null) return null;
        // Walk up: find the layout whose Items is the located list.
        FormLayout? owner = null;
        void Walk(List<FormComponent> list, FormLayout? parent)
        {
            if (ReferenceEquals(list, location.Value.list)) { owner = parent; return; }
            foreach (var c in list)
                if (c is FormLayout l)
                    Walk(l.Items, l);
        }
        Walk(Definition.Components, null);
        return owner;
    }

    public void MoveUp(FormComponent component) => Nudge(component, -1);
    public void MoveDown(FormComponent component) => Nudge(component, +1);

    private void Nudge(FormComponent component, int delta)
    {
        var location = FindParentList(component.Id);
        if (location == null) return;
        var (list, index) = location.Value;
        var newIndex = index + delta;
        if (newIndex < 0 || newIndex >= list.Count) return;
        (list[index], list[newIndex]) = (list[newIndex], list[index]);
        MarkDirty();
    }

    // ── Persistence ────────────────────────────────────────────────

    public async Task LoadAsync(Guid id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var doc = await dbContext.FormDesigns.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (doc?.Definition == null) return;

        Definition = doc.Definition;
        Name = doc.Name;
        Description = doc.Description;
        StoredFormId = doc.Id;
        SelectedComponent = null;
        HasUnsavedChanges = false;
        Notify();
    }

    public async Task SaveAsync()
    {
        var currentUser = _httpContextAccessor.HttpContext?.User;
        var updatedBy = currentUser?.Identity?.Name ?? "Unknown";

        // Detach the stored copy from the live designer instance.
        var definitionCopy = CloneDefinition(Definition);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var existing = StoredFormId.HasValue
            ? await dbContext.FormDesigns.FindAsync(StoredFormId.Value)
            : null;

        var isNew = existing == null;

        if (existing != null)
        {
            existing.Definition = definitionCopy;
            existing.Name = Name;
            existing.Description = Description;
            existing.ObjectKey = definitionCopy.ObjectKey;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var doc = new FormDesign
            {
                Id = Guid.NewGuid(),
                Name = Name,
                Description = Description,
                ObjectKey = definitionCopy.ObjectKey,
                Definition = definitionCopy,
                CreatedBy = updatedBy
            };
            dbContext.FormDesigns.Add(doc);
            StoredFormId = doc.Id;
        }

        await dbContext.SaveChangesAsync();

        if (isNew && StoredFormId.HasValue)
        {
            var userId = currentUser?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
                await _ownershipService.GrantOwnershipAsync("Form", StoredFormId.Value, userId);
        }

        HasUnsavedChanges = false;
        Notify();
    }

    private static FormDefinition CloneDefinition(FormDefinition definition)
    {
        var json = JsonSerializer.Serialize(definition, FormSerializer.Options);
        return JsonSerializer.Deserialize<FormDefinition>(json) ?? definition;
    }

    public void Reset()
    {
        Definition = NewDefinition();
        Name = "New Form";
        Description = "";
        StoredFormId = null;
        SelectedComponent = null;
        HasUnsavedChanges = false;
        EndDrag();
    }
}
