namespace Argent.Models.DomainObjects.Querying;

/// <summary>Filtering, sorting, and paging for a record query. All parts are optional.</summary>
public class DomainQuery
{
    public DomainFilter? Filter { get; set; }
    public List<DomainSort> Sort { get; set; } = [];

    /// <summary>Number of records to skip (paging offset).</summary>
    public int? Skip { get; set; }

    /// <summary>Maximum number of records to return (page size).</summary>
    public int? Take { get; set; }
}
