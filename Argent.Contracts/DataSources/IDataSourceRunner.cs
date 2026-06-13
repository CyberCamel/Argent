using Argent.Models.DataSources;

namespace Argent.Contracts.DataSources;

/// <summary>
/// The single entry point for running requests against stored data sources. Resolves a data
/// source by key, dispatches to the matching provider, and returns a uniform result. Consumed
/// by forms, the domain object store, the workflow engine, and the future LookupField.
/// </summary>
public interface IDataSourceRunner
{
    Task<DataSourceResult> ExecuteAsync(
        string dataSourceKey,
        DataSourceRequest request,
        IDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    Task<DataSourceTestResult> TestAsync(string dataSourceKey, CancellationToken cancellationToken = default);

    /// <summary>Tests an in-memory data source that may not be persisted yet (admin "test before save").</summary>
    Task<DataSourceTestResult> TestAsync(DataSource dataSource, CancellationToken cancellationToken = default);
}
