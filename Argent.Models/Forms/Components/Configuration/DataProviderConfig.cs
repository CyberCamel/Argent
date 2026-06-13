namespace Argent.Models.Forms.Components.Configuration;

public class DataProviderConfig
{
    public string? DataSource { get; set; }
    public List<string> DependsOn { get; set; } = [];

    /// <summary>System key of the DomainObject to populate items from (resolved via IDomainObjectStore).</summary>
    public string? DomainObjectId { get; set; }

    /// <summary>Index into the DomainObjectDefinition's DataSources list.</summary>
    public int? DataSourceIndex { get; set; }

    /// <summary>Domain property or source column to use as the option label.</summary>
    public string? LabelField { get; set; }

    /// <summary>Domain property or source column to use as the option value.</summary>
    public string? ValueField { get; set; }
}
