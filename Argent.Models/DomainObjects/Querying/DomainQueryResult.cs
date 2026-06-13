namespace Argent.Models.DomainObjects.Querying;

/// <summary>A page of records plus the total match count, for grids with paging.</summary>
public class DomainQueryResult
{
    public IReadOnlyList<DomainRecord> Records { get; set; } = [];

    /// <summary>Total records matching the filter, ignoring paging.</summary>
    public int TotalCount { get; set; }
}
