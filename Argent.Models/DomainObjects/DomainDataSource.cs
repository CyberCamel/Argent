namespace Argent.Models.DomainObjects;

/// <summary>
/// A named binding to an external SQL source that yields rows in this object's shape.
/// The managed JSON store remains the source of truth; data sources are read-oriented
/// (populate dropdowns/grids, import) and are referenced by index from
/// <c>DataProviderConfig.DataSourceIndex</c> on the Forms side.
/// </summary>
public class DomainDataSource
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Key of the admin-defined <c>DataSource</c> connection this query runs against.</summary>
    public string DataSourceKey { get; set; } = string.Empty;

    /// <summary>The SQL query that returns rows shaped like this object.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Maps a result column name to a <see cref="DomainProperty.Key"/>. Unmapped columns fall through by matching name.</summary>
    public Dictionary<string, string> ColumnMap { get; set; } = [];
}
