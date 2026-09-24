using Argent.Core.Data;

namespace Argent.Core;

public interface IDbService
{
    public Dictionary<string, object> InvokeQuery(string query, SqlDataSource ds);
    public Task<Dictionary<string, object>> InvokeQueryAsync(string query , SqlDataSource ds);
}