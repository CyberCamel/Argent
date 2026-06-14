using Argent.Models.Attributes;

namespace Argent.Models.DomainObjects;

/// <summary>
/// A named binding to an external SQL source that yields rows in this object's shape.
/// The managed JSON store remains the source of truth; data sources are read-oriented
/// (populate dropdowns/grids, import) and are referenced by index from
/// <c>DataProviderConfig.DataSourceIndex</c> on the Forms side.
/// </summary>

[PbacResource]
public class DomainDataSource
{
    [PbacProperty]
    public string Name { get; set; } = string.Empty;
    [PbacProperty]
    public string? Description { get; set; }

    /// <summary>Key of the admin-defined <c>DataSource</c> connection this query runs against.</summary>
    [PbacProperty]
    public string DataSourceKey { get; set; } = string.Empty;

    /// <summary>The SQL query that returns rows shaped like this object.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Maps result columns to property keys. Unmapped columns fall through by matching name.</summary>
    public List<DomainColumnMapping> ColumnMappings { get; set; } = [];
}

/// <summary>Maps one result column from a <see cref="DomainDataSource"/> query onto a property key.</summary>
public class DomainColumnMapping
{
    public string Column { get; set; } = string.Empty;
    public string Property { get; set; } = string.Empty;
}
