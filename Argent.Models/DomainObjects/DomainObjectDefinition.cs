namespace Argent.Models.DomainObjects;

/// <summary>
/// The serialized shape of a domain object: its properties, external data sources, and
/// display metadata. Stored as JSON inside a <see cref="DomainObjectVersion"/> or
/// <see cref="DomainObjectDraft"/>, mirroring how workflow/form definitions are persisted.
/// </summary>
[Serializable]
public class DomainObjectDefinition
{
    /// <summary>Stable system name used to look the object up from forms, workflows, and scripts (e.g. "customer").</summary>
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string PluralName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Property key used to label a record in lists/dropdowns. Defaults to the first text property when unset.</summary>
    public string? TitleProperty { get; set; }

    public List<DomainProperty> Properties { get; set; } = [];

    public List<DomainDataSource> DataSources { get; set; } = [];
}
