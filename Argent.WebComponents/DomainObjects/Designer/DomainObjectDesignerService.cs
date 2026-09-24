using Argent.Core.DataSources;
using Argent.Core.DomainObjects;
using Argent.Core.DomainObjects;

namespace Argent.WebComponents.DomainObjects.Designer;

public class DomainObjectDesignerService(
    IDomainObjectDefinitionService _definitions,
    IDataSourceCatalog _dataSources)
{
    public Guid? StoredObjectId { get; private set; }
    public DomainObjectDefinition Definition { get; private set; } = NewDefinition();
    public DomainProperty? SelectedProperty { get; private set; }
    public bool HasUnsavedChanges { get; private set; }
    public string? PublishedVersion { get; private set; }
    public bool IsReadOnly { get; private set; }
    public bool HasDraft { get; private set; }
    public Guid? LoadedVersionId { get; private set; }
    public List<DomainObjectVersion> Versions { get; private set; } = [];

    public IReadOnlyList<DomainObjectSummary> Catalog { get; private set; } = [];
    public IReadOnlyList<DataSourceSummary> DataSourceCatalog { get; private set; } = [];

    public bool IsNew => StoredObjectId is null;

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
        IsReadOnly = false;
        HasDraft = false;
        LoadedVersionId = null;
        Versions = [];
    }

    public async Task EnsureCatalogAsync()
    {
        Catalog = await _definitions.GetSummariesAsync();
        DataSourceCatalog = await _dataSources.GetSummariesAsync();
    }

    public async Task LoadAsync(Guid id)
    {
        var header = await _definitions.GetAsync(id);
        if (header is null) return;

        Definition = await _definitions.GetWorkingDefinitionAsync(id)
                     ?? new DomainObjectDefinition { Key = header.Key, DisplayName = header.Name };
        StoredObjectId = id;
        SelectedProperty = null;
        HasUnsavedChanges = false;

        Versions = await _definitions.GetVersionsAsync(id);
        PublishedVersion = Versions.FirstOrDefault()?.Version.ToString();
        IsReadOnly = false;
        HasDraft = true;
        LoadedVersionId = null;
        Notify();
    }

    public async Task CreateDraftFromVersionAsync(Guid versionId)
    {
        if (!StoredObjectId.HasValue) return;

        var version = await _definitions.GetVersionAsync(versionId);
        if (version == null) return;

        Definition = version.Definition;
        await _definitions.SaveDraftAsync(StoredObjectId.Value, Definition);
        IsReadOnly = false;
        LoadedVersionId = null;
        HasUnsavedChanges = false;
        Notify();
    }

    public async Task LoadVersionAsync(Guid versionId)
    {
        var version = await _definitions.GetVersionAsync(versionId);
        if (version is null) return;

        var header = await _definitions.GetAsync(version.DomainObjectId);
        if (header is null) return;

        Definition = version.Definition;
        StoredObjectId = version.DomainObjectId;
        SelectedProperty = null;
        HasUnsavedChanges = false;
        IsReadOnly = true;
        HasDraft = false; // not known without a DB call; disable "Create Draft" button when viewing versions
        LoadedVersionId = versionId;
        Name = header.Name;
        Notify();
    }

    public async Task SwitchToDraftAsync()
    {
        if (StoredObjectId.HasValue)
            await LoadAsync(StoredObjectId.Value);
    }

    public async Task SaveAsync()
    {
        if (IsReadOnly) return;
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
        await SaveAsync();
        var version = await _definitions.PublishAsync(StoredObjectId!.Value);
        PublishedVersion = version.Version.ToString();
        HasUnsavedChanges = false;
        Notify();
    }

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

    public void AddDataSource()
    {
        Definition.DataSources.Add(new DomainDataSource { Name = "New Data Source" });
        MarkDirty();
    }

    public void RemoveDataSource(DomainDataSource dataSource)
    {
        Definition.DataSources.Remove(dataSource);
        MarkDirty();
    }

    public void AddColumnMapping(DomainDataSource dataSource)
    {
        dataSource.ColumnMappings.Add(new DomainColumnMapping());
        MarkDirty();
    }

    public void RemoveColumnMapping(DomainDataSource dataSource, DomainColumnMapping mapping)
    {
        dataSource.ColumnMappings.Remove(mapping);
        MarkDirty();
    }
}
