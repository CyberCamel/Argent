using Argent.Models.DataSources;

namespace Argent.Contracts.DataSources;

/// <summary>
/// Executes and probes one kind of connection (SQL/REST/SOAP). Stateless: all config comes
/// from the <see cref="DataSource"/> argument, so a single instance serves every connection
/// of its kind. The runner dispatches to providers by <see cref="Kind"/>.
/// </summary>
public interface IDataSourceProvider
{
    DataSourceKind Kind { get; }

    Task<DataSourceResult> ExecuteAsync(
        DataSource dataSource,
        DataSourceRequest request,
        IDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);

    Task<DataSourceTestResult> TestAsync(DataSource dataSource, CancellationToken cancellationToken = default);
}
