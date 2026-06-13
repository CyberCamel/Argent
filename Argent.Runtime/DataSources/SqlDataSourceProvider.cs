using System.Diagnostics;
using Argent.Contracts.DataSources;
using Argent.Models.DataSources;
using Microsoft.Data.SqlClient;

namespace Argent.Runtime.DataSources;

public class SqlDataSourceProvider : IDataSourceProvider
{
    public DataSourceKind Kind => DataSourceKind.Sql;

    public async Task<DataSourceResult> ExecuteAsync(
        DataSource dataSource, DataSourceRequest request, IDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        if (dataSource is not SqlDataSource sql) return DataSourceResult.Fail("Data source is not a SQL connection.");
        if (request is not SqlRequest req) return DataSourceResult.Fail("Request is not a SQL request.");

        try
        {
            await using var connection = new SqlConnection(sql.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(req.Query, connection);

            // Static request defaults first, then runtime parameters override by name.
            var merged = new Dictionary<string, object?>(req.Parameters);
            foreach (var (key, value) in parameters) merged[key] = value;
            foreach (var (key, value) in merged)
                command.Parameters.AddWithValue("@" + key.TrimStart('@'), value ?? DBNull.Value);

            var result = new DataSourceResult { Success = true };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (reader.FieldCount > 0)
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object?>(reader.FieldCount);
                    for (var i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    result.Rows.Add(row);
                }
            }
            result.RowsAffected = reader.RecordsAffected;
            return result;
        }
        catch (Exception ex)
        {
            return DataSourceResult.Fail(ex.Message);
        }
    }

    public async Task<DataSourceTestResult> TestAsync(DataSource dataSource, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (dataSource is not SqlDataSource sql)
                return new DataSourceTestResult { Success = false, Message = "Not a SQL data source." };

            await using var connection = new SqlConnection(sql.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            return new DataSourceTestResult { Success = true, Message = "Connection succeeded.", ElapsedMilliseconds = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            return new DataSourceTestResult { Success = false, Message = ex.Message, ElapsedMilliseconds = sw.ElapsedMilliseconds };
        }
    }
}
