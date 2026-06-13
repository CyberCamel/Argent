using Argent.Models.DataSources;

namespace Argent.Contracts.DataSources;

/// <summary>The outcome of executing a request against a data source.</summary>
public class DataSourceResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    /// <summary>Tabular rows projected from the response (SQL result set, JSON array, etc.).</summary>
    public List<Dictionary<string, object?>> Rows { get; set; } = [];

    /// <summary>The raw response payload (JSON/XML), for consumers that want to parse it themselves.</summary>
    public string? Raw { get; set; }

    public int RowsAffected { get; set; }

    public static DataSourceResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>The outcome of a connectivity/auth probe against a data source.</summary>
public class DataSourceTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public long ElapsedMilliseconds { get; set; }
}

/// <summary>Lightweight listing row for the data source admin catalog.</summary>
public class DataSourceSummary
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DataSourceKind Kind { get; set; }
}
