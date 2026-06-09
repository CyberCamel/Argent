namespace Argent.Contracts.Workflows.Execution;

public class SqlExecutionResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int RowsAffected { get; set; }
    public List<Dictionary<string, object?>>? Rows { get; set; }
}

public interface IDataSourceService
{
    Task<SqlExecutionResult> ExecuteAsync(string connectionKey, string query, Dictionary<string, string>? parameters = null, bool useTransaction = false);
}
