using Argent.Models.DataSources;

namespace Argent.Infrastructure.Data;

/// <summary>
/// EF persistence for a data source. Metadata is stored in columns for listing; the full
/// connection config (including secrets) is serialized to JSON and encrypted into
/// <see cref="Config"/> by the catalog before saving.
/// </summary>
public class DataSourceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DataSourceKind Kind { get; set; }

    /// <summary>Encrypted JSON of the polymorphic <see cref="DataSource"/>.</summary>
    public string Config { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
