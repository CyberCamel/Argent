using Argent.Contracts.DomainObjects;
using Argent.Models.DomainObjects;

namespace Argent.Runtime.DomainObjects;

/// <summary>
/// Scoped editing state for the domain object designer, mirroring <c>FormDesignerService</c>:
/// holds the working definition + selection + dirty flag, raises <see cref="OnChange"/> so the
/// Blazor component re-renders, and wraps <see cref="IDomainObjectDefinitionService"/> for the
/// draft/publish lifecycle.
/// </summary>
public class DomainObjectDesignerService(IDomainObjectDefinitionService _definitions)
{
    public Guid? StoredObjectId { get; private set; }
    public DomainObjectDefinition Definition { get; private set; } = NewDefinition();
    public DomainProperty? SelectedProperty { get; private set; }
    public bool HasUnsavedChanges { get; private set; }
    public string? PublishedVersion { get; private set; }

    /// <summary>Other domain objects, for reference-target and title pickers. Loaded once.</summary>
    public IReadOnlyList<DomainObjectSummary> Catalog { get; private set; } = [];

    public bool IsNew => StoredObjectId is null;

    // Header fields are projected onto the definition (the draft's Name is synced from DisplayName on save).
    public string Name { get => Definition.DisplayName; set => Definition.DisplayName = value; }
    public string Key { get => Definition.Key; set => Definition.Key = value; }
    public string Description { get => Definition.Description ?? string.Empty; set => Definition.Description = value; }

    public event Action? OnChange;
    public void Notify() => OnChange?.Invoke();
    public void MarkDirty() { HasUnsavedChanges = true; Notify(); }

    private static DomainObjectDefinition NewDefinition() => new() { DisplayName = "New Domain Object" };

    public void Reset()
    {
        StoredObjectId = null;
        Definition = NewDefinition();
        SelectedProperty = null;
        HasUnsavedChanges = false;
        PublishedVersion = null;
    }

    public async Task EnsureCatalogAsync() => Catalog = await _definitions.GetSummariesAsync();

    public async Task LoadAsync(Guid id)
    {
        var header = await _definitions.GetAsync(id);
        if (header is null) return;

        Definition = await _definitions.GetWorkingDefinitionAsync(id)
                     ?? new DomainObjectDefinition { Key = header.Key, DisplayName = header.Name };
        StoredObjectId = id;
        SelectedProperty = null;
        HasUnsavedChanges = false;

        var versions = await _definitions.GetVersionsAsync(id);
        PublishedVersion = versions.FirstOrDefault()?.Version.ToString();
        Notify();
    }

    /// <summary>Creates the object on first save, then persists the working definition as its draft.</summary>
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Key))
            throw new InvalidOperationException("A system key is required before saving.");
        Key = Key.Trim();

        if (StoredObjectId is null)
        {
            var created = await _definitions.CreateAsync(Key, Name, Description);
            StoredObjectId = created.Id;
        }

        await _definitions.SaveDraftAsync(StoredObjectId.Value, Definition);
        HasUnsavedChanges = false;
        Notify();
    }

    public async Task PublishAsync()
    {
        // Always flush the draft first so there is something to publish and the latest edits are included.
        await SaveAsync();
        var version = await _definitions.PublishAsync(StoredObjectId!.Value);
        PublishedVersion = version.Version.ToString();
        HasUnsavedChanges = false;
        Notify();
    }

    // ── Property operations ────────────────────────────────────────

    public void SelectProperty(DomainProperty? property)
    {
        SelectedProperty = property;
        Notify();
    }

    public void AddProperty()
    {
        var property = new DomainProperty
        {
            Key = UniqueKey("field"),
            DisplayName = "New Property",
            Type = DomainPropertyType.Text
        };
        Definition.Properties.Add(property);
        SelectedProperty = property;
        MarkDirty();
    }

    public void RemoveProperty(DomainProperty property)
    {
        Definition.Properties.Remove(property);
        if (ReferenceEquals(SelectedProperty, property)) SelectedProperty = null;
        if (Definition.TitleProperty == property.Key) Definition.TitleProperty = null;
        MarkDirty();
    }

    public void MoveProperty(DomainProperty property, int delta)
    {
        var index = Definition.Properties.IndexOf(property);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Definition.Properties.Count) return;
        (Definition.Properties[index], Definition.Properties[target]) =
            (Definition.Properties[target], Definition.Properties[index]);
        MarkDirty();
    }

    public string UniqueKey(string baseKey)
    {
        var keys = Definition.Properties.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!keys.Contains(baseKey)) return baseKey;
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseKey}{i}";
            if (!keys.Contains(candidate)) return candidate;
        }
    }
}
