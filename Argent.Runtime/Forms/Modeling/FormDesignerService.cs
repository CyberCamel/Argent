using Argent.Infrastructure.Data;
using Argent.Models.Forms.Components;
using Argent.Models.Forms.Components.Base;
using Argent.Models.Forms.Components.Configuration;
using Microsoft.AspNetCore.Http;

namespace Argent.Runtime.Forms.Modeling;

public class ToolboxItem
{
    public required string TypeName { get; set; }
    public required string DisplayName { get; set; }
    public required string Icon { get; set; }
    public required Func<FormComponent> Factory { get; set; }
}

public class FormDesignerService(
    ArgentDbContext _dbContext,
    IHttpContextAccessor _httpContextAccessor)
{
    public FormDefinition Definition { get; set; } = new()
    {
        FormId = Guid.NewGuid().ToString(),
        Components = []
    };

    public FormComponent? SelectedComponent { get; set; }

    // Drag-and-drop state
    public bool IsDragging { get; set; }
    public string? DragPayload { get; set; }
    public string? HoverComponentId { get; set; }
    public int HoverDropIndex { get; set; } = -1;
    public bool HoverInsideContainer { get; set; }
    public int HoverColumnIndex { get; set; } = -1;

    public Guid? StoredFormId { get; set; }
    public string Name { get; set; } = "New Form";
    public string Description { get; set; } = "";

    public static readonly List<ToolboxItem> ToolboxItems =
    [
        new() { TypeName = "TextField",     DisplayName = "Text Field",   Icon = "bi-input-cursor-text",   Factory = () => new FormField { Xtype = "TextField",     Name = "field_" + Guid.NewGuid().ToString("N")[..8], FieldLabel = "Text Field" } },
        new() { TypeName = "NumericField",  DisplayName = "Numeric",      Icon = "bi-123",                 Factory = () => new FormField { Xtype = "NumericField",  Name = "field_" + Guid.NewGuid().ToString("N")[..8], FieldLabel = "Numeric" } },
        new() { TypeName = "DropdownField", DisplayName = "Dropdown",     Icon = "bi-menu-down",           Factory = () => new FormField { Xtype = "DropdownField", Name = "field_" + Guid.NewGuid().ToString("N")[..8], FieldLabel = "Dropdown", Items = [new SelectOption { Label = "Option 1", Value = "opt1" }, new SelectOption { Label = "Option 2", Value = "opt2" }] } },
        new() { TypeName = "CheckboxField", DisplayName = "Checkbox",     Icon = "bi-check-square",        Factory = () => new FormField { Xtype = "CheckboxField", Name = "field_" + Guid.NewGuid().ToString("N")[..8], FieldLabel = "Checkbox", Value = false } },
        new() { TypeName = "HtmlBox",       DisplayName = "HTML Block",   Icon = "bi-code-slash",          Factory = () => new FormLayout { Xtype = "HtmlBox", Title = "<p>Enter HTML content here</p>" } },
        new() { TypeName = "Row",           DisplayName = "Row",          Icon = "bi-arrows-expand",       Factory = () => new FormLayout { Xtype = "Row",    Direction = "row" } },
        new() { TypeName = "Column",        DisplayName = "Column",       Icon = "bi-arrows-vertical",     Factory = () => new FormLayout { Xtype = "Column", Direction = "column" } },
        new() { TypeName = "Flex",          DisplayName = "Flex Box",     Icon = "bi-layout-three-columns",Factory = () => new FormLayout { Xtype = "Flex", Direction = "row", Gap = 3 } },
        new() { TypeName = "Fieldset",      DisplayName = "Fieldset",     Icon = "bi-border-all",          Factory = () => new FormLayout { Xtype = "Fieldset", Title = "Fieldset", LayoutType = LayoutType.Fieldset, Direction = "column" } },
        new() { TypeName = "Tabs",          DisplayName = "Tabs",         Icon = "bi-files",               Factory = () => new FormLayout { Xtype = "Tabs", LayoutType = LayoutType.Tabs } },
        new() { TypeName = "Accordion",     DisplayName = "Accordion",    Icon = "bi-arrows-collapse",     Factory = () => new FormLayout { Xtype = "Accordion", LayoutType = LayoutType.Accordion } },
    ];

    public event Action? OnChange;

    public void Notify() => OnChange?.Invoke();

    public void AddComponent(FormComponent component, int? index = null)
    {
        if (index.HasValue)
            Definition.Components.Insert(index.Value, component);
        else
            Definition.Components.Add(component);
        SelectedComponent = component;
        Notify();
    }

    public void RemoveComponent(FormComponent component)
    {
        if (TryRemoveComponent(component, Definition.Components))
        {
            if (SelectedComponent == component)
                SelectedComponent = Definition.Components.LastOrDefault();
            Notify();
        }
    }

    private bool TryRemoveComponent(FormComponent target, List<FormComponent> list)
    {
        if (list.Remove(target))
            return true;
        foreach (var c in list)
        {
            if (c is FormLayout layout && TryRemoveComponent(target, layout.Items))
                return true;
        }
        return false;
    }

    public void MoveComponent(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Definition.Components.Count) return;
        if (toIndex < 0 || toIndex >= Definition.Components.Count) return;
        if (fromIndex == toIndex) return;

        var item = Definition.Components[fromIndex];
        Definition.Components.RemoveAt(fromIndex);
        Definition.Components.Insert(toIndex, item);
        Notify();
    }

    // ── Drag‑and‑drop ──────────────────────────────────────────────

    public void BeginDrag(string payload)
    {
        IsDragging = true;
        DragPayload = payload;
        Notify();
    }

    public void EndDrag()
    {
        IsDragging = false;
        DragPayload = null;
        HoverComponentId = null;
        HoverDropIndex = -1;
        HoverInsideContainer = false;
        HoverColumnIndex = -1;
        Notify();
    }

    public void SetHover(int index)
    {
        HoverDropIndex = index;
        HoverComponentId = null;
        HoverInsideContainer = false;
        Notify();
    }

    public void SetHoverInside(string containerId, int columnIndex = -1)
    {
        HoverDropIndex = -1;
        HoverComponentId = containerId;
        HoverInsideContainer = true;
        HoverColumnIndex = columnIndex;
        Notify();
    }

    public void ClearHover()
    {
        HoverDropIndex = -1;
        HoverComponentId = null;
        HoverInsideContainer = false;
        HoverColumnIndex = -1;
        Notify();
    }

    public void DropAt(int index)
    {
        if (string.IsNullOrEmpty(DragPayload)) return;
        var parts = DragPayload.Split(':');
        if (parts[0] == "add")
            DropAdd(parts[1], index);
        else if (parts[0] == "move")
            DropMove(parts[1], index);
        EndDrag();
    }

    public void DropIntoContainer(string containerId, int columnIndex = -1)
    {
        if (string.IsNullOrEmpty(DragPayload)) return;
        var container = FindComponent(containerId, Definition.Components) as FormLayout;
        if (container == null) return;

        var parts = DragPayload.Split(':');
        if (parts[0] == "add")
        {
            var comp = CreateComponent(parts[1]);
            if (comp != null)
            {
                comp.ColumnIndex = columnIndex >= 0 ? columnIndex : 0;
                container.Items.Add(comp);
                SelectedComponent = comp;
                Notify();
            }
        }
        else if (parts[0] == "move")
        {
            var comp = FindComponent(parts[1], Definition.Components);
            if (comp != null)
            {
                var (parentList, idx) = FindParentList(parts[1], Definition.Components);
                parentList?.RemoveAt(idx);
                comp.ColumnIndex = columnIndex >= 0 ? columnIndex : 0;
                container.Items.Add(comp);
                SelectedComponent = comp;
                Notify();
            }
        }
        EndDrag();
    }

    private void DropAdd(string xtype, int index)
    {
        var comp = CreateComponent(xtype);
        if (comp == null) return;
        if (index >= 0 && index <= Definition.Components.Count)
            Definition.Components.Insert(index, comp);
        else
            Definition.Components.Add(comp);
        SelectedComponent = comp;
        Notify();
    }

    private FormComponent? CreateComponent(string xtype)
    {
        var template = ToolboxItems.FirstOrDefault(t => t.TypeName == xtype);
        return template?.Factory();
    }

    private void DropMove(string componentId, int targetIndex)
    {
        var (parentList, oldIndex) = FindParentList(componentId, Definition.Components);
        if (parentList == null) return;
        var comp = parentList[oldIndex];

        if (parentList == Definition.Components && oldIndex < targetIndex)
            targetIndex--;

        parentList.RemoveAt(oldIndex);

        if (targetIndex >= 0 && targetIndex <= Definition.Components.Count)
            Definition.Components.Insert(targetIndex, comp);
        else
            Definition.Components.Add(comp);

        SelectedComponent = comp;
        Notify();
    }

    private FormComponent? FindComponent(string id, List<FormComponent> list)
    {
        foreach (var c in list)
        {
            if (c.Id == id) return c;
            if (c is FormLayout l)
            {
                var found = FindComponent(id, l.Items);
                if (found != null) return found;
            }
        }
        return null;
    }

    private (List<FormComponent>? list, int index) FindParentList(string id, List<FormComponent> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == id) return (list, i);
            if (list[i] is FormLayout l)
            {
                var (found, idx) = FindParentList(id, l.Items);
                if (found != null) return (found, idx);
            }
        }
        return (null, -1);
    }

    public void SelectComponent(FormComponent? component)
    {
        SelectedComponent = component;
        Notify();
    }

    public void RemoveSelectedComponent()
    {
        if (SelectedComponent != null)
            RemoveComponent(SelectedComponent);
    }

    public void UpdateSelectedName(string value)
    {
        if (SelectedComponent is FormField field)
        {
            field.Name = value;
            Notify();
        }
    }

    public void UpdateSelectedFieldLabel(string? value)
    {
        if (SelectedComponent is FormField field)
        {
            field.FieldLabel = value;
            Notify();
        }
    }

    public void UpdateSelectedDescription(string? value)
    {
        if (SelectedComponent is FormField field)
        {
            field.Description = value;
            Notify();
        }
    }

    public void UpdateSelectedAllowBlank(bool value)
    {
        if (SelectedComponent is FormField field)
        {
            field.AllowBlank = value;
            Notify();
        }
    }

    public void UpdateSelectedValue(object? value)
    {
        if (SelectedComponent is FormField field)
        {
            field.Value = value;
            Notify();
        }
    }

    public void UpdateSelectedPlaceholder(string? value)
    {
        if (SelectedComponent is FormField field)
        {
            field.Placeholder = value;
            Notify();
        }
    }

    public void UpdateSelectedDisabled(bool value)
    {
        if (SelectedComponent is FormField field)
        {
            field.Disabled = value;
            Notify();
        }
    }

    public void UpdateSelectedReadOnly(bool value)
    {
        if (SelectedComponent is FormField field)
        {
            field.ReadOnly = value;
            Notify();
        }
    }

    public void UpdateSelectedSpan(int value)
    {
        if (SelectedComponent is FormField field)
        {
            field.Span = Math.Clamp(value, 1, 12);
            Notify();
        }
    }

    public void UpdateSelectedOrder(int value)
    {
        if (SelectedComponent is FormField field)
        {
            field.Order = Math.Max(0, value);
            Notify();
        }
    }

    public void UpdateSelectedHtml(string? value)
    {
        if (SelectedComponent is FormLayout layout)
        {
            layout.Title = value;
            Notify();
        }
    }

    public void UpdateSelectedCssClass(string? value)
    {
        if (SelectedComponent != null)
        {
            SelectedComponent.CssClass = value;
            Notify();
        }
    }

    public void UpdateSelectedTitle(string? value)
    {
        if (SelectedComponent is FormLayout layout)
        {
            layout.Title = value;
            Notify();
        }
    }

    public void UpdateSelectedLayoutDirection(string? value)
    {
        if (SelectedComponent is FormLayout layout)
        {
            layout.Direction = value ?? "row";
            Notify();
        }
    }

    public void UpdateSelectedLayoutGap(int value)
    {
        if (SelectedComponent is FormLayout layout)
        {
            layout.Gap = Math.Max(0, value);
            Notify();
        }
    }

    public void UpdateSelectedLayoutAlign(string? value)
    {
        if (SelectedComponent is FormLayout layout)
        {
            layout.Align = value;
            Notify();
        }
    }

    public void UpdateSelectedLayoutJustify(string? value)
    {
        if (SelectedComponent is FormLayout layout)
        {
            layout.Justify = value;
            Notify();
        }
    }

    public void UpdateSelectedLayoutWrap(bool value)
    {
        if (SelectedComponent is FormLayout layout)
        {
            layout.Wrap = value;
            Notify();
        }
    }

    public void UpdateSelectedColumns(int value)
    {
        if (SelectedComponent is FormLayout layout)
        {
            layout.Columns = Math.Max(1, value);
            Notify();
        }
    }

    public void UpdateSelectedColumnIndex(int value)
    {
        if (SelectedComponent != null)
        {
            SelectedComponent.ColumnIndex = Math.Max(0, value);
            Notify();
        }
    }

    public async Task LoadAsync(Guid id)
    {
        var doc = await _dbContext.FormDocuments.FindAsync(id);
        if (doc?.Definition != null)
        {
            Definition = doc.Definition;
            Name = doc.Name;
            Description = doc.Description;
            StoredFormId = doc.Id;
            SelectedComponent = null;
            Notify();
        }
    }

    public async Task SaveAsync()
    {
        var currentUser = _httpContextAccessor.HttpContext?.User;
        var updatedBy = currentUser?.Identity?.Name ?? "Unknown";

        var json = System.Text.Json.JsonSerializer.Serialize(Definition);
        var definitionCopy = System.Text.Json.JsonSerializer.Deserialize<FormDefinition>(json) ?? Definition;

        if (StoredFormId.HasValue)
        {
            var existing = await _dbContext.FormDocuments.FindAsync(StoredFormId.Value);
            if (existing != null)
            {
                existing.Definition = definitionCopy;
                existing.Name = Name;
                existing.Description = Description;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var doc = new FormDocument
                {
                    Id = Guid.NewGuid(),
                    Name = Name,
                    Description = Description,
                    Definition = definitionCopy,
                    CreatedBy = updatedBy
                };
                _dbContext.FormDocuments.Add(doc);
                StoredFormId = doc.Id;
            }
        }
        else
        {
            var doc = new FormDocument
            {
                Id = Guid.NewGuid(),
                Name = Name,
                Description = Description,
                Definition = definitionCopy,
                CreatedBy = updatedBy
            };
            _dbContext.FormDocuments.Add(doc);
            StoredFormId = doc.Id;
        }

        await _dbContext.SaveChangesAsync();
    }

    public void Reset()
    {
        Definition = new FormDefinition
        {
            FormId = Guid.NewGuid().ToString(),
            Components = []
        };
        Name = "New Form";
        Description = "";
        StoredFormId = null;
        SelectedComponent = null;
        HoverDropIndex = -1;
        HoverComponentId = null;
        HoverInsideContainer = false;
        HoverColumnIndex = -1;
        IsDragging = false;
        DragPayload = null;
        Notify();
    }
}