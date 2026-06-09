using Argent.Models.Data;

namespace Argent.Contracts;

public interface IDbService
{
    public Dictionary<string, object> InvokeQuery(string query, SqlDataSource ds);
    public Task<Dictionary<string, object>> InvokeQueryAsync(string query , SqlDataSource ds);
}