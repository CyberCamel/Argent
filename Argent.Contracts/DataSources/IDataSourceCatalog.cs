using Argent.Models.DataSources;

namespace Argent.Contracts.DataSources;

/// <summary>
/// Admin CRUD over stored data sources. Secret fields are encrypted at rest; returned
/// <see cref="DataSource"/> instances are decrypted and ready to use.
/// </summary>
public interface IDataSourceCatalog
{
    Task<List<DataSourceSummary>> GetSummariesAsync();
    Task<DataSource?> GetAsync(Guid id);
    Task<DataSource?> GetByKeyAsync(string key);
    Task<Guid> SaveAsync(DataSource dataSource, Guid? id = null, string? user = null);
    Task DeleteAsync(Guid id);
}
