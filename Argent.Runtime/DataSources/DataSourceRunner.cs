using Argent.Contracts.DataSources;
using Argent.Models.DataSources;

namespace Argent.Runtime.DataSources;

/// <summary>
/// Resolves a stored data source by key and dispatches to the provider for its kind. The one
/// entry point forms, the domain object store, the workflow engine, and LookupField call.
/// </summary>
public class DataSourceRunner : IDataSourceRunner
{
    private readonly IDataSourceCatalog _catalog;
    private readonly Dictionary<DataSourceKind, IDataSourceProvider> _providers;

    public DataSourceRunner(IDataSourceCatalog catalog, IEnumerable<IDataSourceProvider> providers)
    {
        _catalog = catalog;
        _providers = providers.ToDictionary(p => p.Kind);
    }

    public async Task<DataSourceResult> ExecuteAsync(
        string dataSourceKey, DataSourceRequest request, IDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        var dataSource = await _catalog.GetByKeyAsync(dataSourceKey);
        if (dataSource is null) return DataSourceResult.Fail($"Data source '{dataSourceKey}' not found.");
        if (!_providers.TryGetValue(dataSource.Kind, out var provider))
            return DataSourceResult.Fail($"No provider registered for {dataSource.Kind} data sources.");

        return await provider.ExecuteAsync(dataSource, request, parameters ?? new Dictionary<string, object?>(), cancellationToken);
    }

    public async Task<DataSourceTestResult> TestAsync(string dataSourceKey, CancellationToken cancellationToken = default)
    {
        var dataSource = await _catalog.GetByKeyAsync(dataSourceKey);
        if (dataSource is null)
            return new DataSourceTestResult { Success = false, Message = $"Data source '{dataSourceKey}' not found." };
        return await TestAsync(dataSource, cancellationToken);
    }

    public Task<DataSourceTestResult> TestAsync(DataSource dataSource, CancellationToken cancellationToken = default)
    {
        if (!_providers.TryGetValue(dataSource.Kind, out var provider))
            return Task.FromResult(new DataSourceTestResult { Success = false, Message = $"No provider for {dataSource.Kind}." });
        return provider.TestAsync(dataSource, cancellationToken);
    }
}
