using Argent.Models.DomainObjects;
using Argent.Models.DomainObjects.Querying;

namespace Argent.Contracts.DomainObjects;

/// <summary>
/// Runtime access to domain object instances (records). Consumed by forms (grids via
/// <see cref="QueryAsync"/>, dropdowns via <see cref="GetOptionsAsync"/>) and by the
/// workflow engine (CRUD + query). Records are addressed by the object's system key.
///
/// Backed by the managed JSON record store. <see cref="QueryDataSourceAsync"/> instead
/// reads an external SQL data source declared on the definition, mapped into the same
/// record shape.
/// </summary>
public interface IDomainObjectStore
{
    Task<DomainRecord?> GetAsync(string objectKey, Guid id);

    /// <summary>Filtered/sorted/paged query over managed records (grids, workflow lookups).</summary>
    Task<DomainQueryResult> QueryAsync(string objectKey, DomainQuery? query = null);

    Task<DomainRecord> CreateAsync(string objectKey, IDictionary<string, object?> values, string? user = null);

    Task<DomainRecord> UpdateAsync(string objectKey, Guid id, IDictionary<string, object?> values, string? user = null);

    /// <summary>Inserts when <see cref="DomainRecord.Id"/> is unknown, otherwise updates. Convenient for form submit.</summary>
    Task<DomainRecord> UpsertAsync(string objectKey, DomainRecord record, string? user = null);

    Task DeleteAsync(string objectKey, Guid id);

    /// <summary>
    /// Projects records to label/value options for a dropdown. When <paramref name="dataSourceIndex"/>
    /// is set, reads from that external <see cref="DomainDataSource"/> instead of managed storage.
    /// </summary>
    Task<List<DomainOption>> GetOptionsAsync(
        string objectKey,
        string valueField,
        string labelField,
        int? dataSourceIndex = null,
        DomainQuery? query = null);

    /// <summary>Reads an external SQL data source declared on the definition, mapped into the record shape.</summary>
    Task<DomainQueryResult> QueryDataSourceAsync(string objectKey, int dataSourceIndex, DomainQuery? query = null);
}
