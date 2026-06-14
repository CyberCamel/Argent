using Argent.Models.Authorization;
using System.Text.Json.Serialization;
using Argent.Models.Attributes;

namespace Argent.Models.DataSources;

/// <summary>
/// An admin-defined, reusable <b>connection</b> (endpoint + credentials) — not a query.
/// Consumers (domain object bindings, lookup fields, workflow activities) supply the
/// request at call time. Polymorphic by kind, stored encrypted at rest by the catalog.
/// </summary>
[PbacResource]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SqlDataSource), "sql")]
[JsonDerivedType(typeof(RestDataSource), "rest")]
[JsonDerivedType(typeof(SoapDataSource), "soap")]
public abstract class DataSource
{
    /// <summary>Stable system key consumers reference (e.g. "argent-db", "crm-api").</summary>
    [PbacProperty]
    public string Key { get; set; } = string.Empty;
    [PbacProperty]
    public string Name { get; set; } = string.Empty;
    [PbacProperty]
    public string? Description { get; set; }

    [JsonIgnore]
    public abstract DataSourceKind Kind { get; }
}
