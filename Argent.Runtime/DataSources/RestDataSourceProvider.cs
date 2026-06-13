using System.Diagnostics;
using System.Text;
using Argent.Contracts.DataSources;
using Argent.Models.DataSources;

namespace Argent.Runtime.DataSources;

public class RestDataSourceProvider(IHttpClientFactory _httpClientFactory) : IDataSourceProvider
{
    public DataSourceKind Kind => DataSourceKind.Rest;

    public async Task<DataSourceResult> ExecuteAsync(
        DataSource dataSource, DataSourceRequest request, IDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        if (dataSource is not RestDataSource rest) return DataSourceResult.Fail("Data source is not a REST connection.");
        if (request is not RestRequest req) return DataSourceResult.Fail("Request is not a REST request.");

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(rest.TimeoutSeconds > 0 ? rest.TimeoutSeconds : 30);

            var url = BuildUrl(rest.BaseUrl, TokenTemplate.Apply(req.Path, parameters), req.Query, parameters);
            using var message = new HttpRequestMessage(new HttpMethod(string.IsNullOrWhiteSpace(req.Method) ? "GET" : req.Method), url);

            foreach (var (key, value) in rest.DefaultHeaders) message.Headers.TryAddWithoutValidation(key, value);
            foreach (var (key, value) in req.Headers) message.Headers.TryAddWithoutValidation(key, TokenTemplate.Apply(value, parameters));
            ApplyAuth(rest, message);

            if (!string.IsNullOrEmpty(req.Body))
                message.Content = new StringContent(TokenTemplate.Apply(req.Body, parameters), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(message, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = new DataSourceResult { Raw = raw, Success = response.IsSuccessStatusCode };
            if (!response.IsSuccessStatusCode)
                result.Error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            else
                result.Rows = JsonRows.Parse(raw, req.RowsPath);
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
            if (dataSource is not RestDataSource rest)
                return new DataSourceTestResult { Success = false, Message = "Not a REST data source." };

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(rest.TimeoutSeconds > 0 ? rest.TimeoutSeconds : 30);
            using var message = new HttpRequestMessage(HttpMethod.Get, rest.BaseUrl);
            foreach (var (key, value) in rest.DefaultHeaders) message.Headers.TryAddWithoutValidation(key, value);
            ApplyAuth(rest, message);

            using var response = await client.SendAsync(message, cancellationToken);
            // A response (even 4xx) means we reached the endpoint; 5xx/connection errors fail.
            return new DataSourceTestResult
            {
                Success = (int)response.StatusCode < 500,
                Message = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                ElapsedMilliseconds = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            return new DataSourceTestResult { Success = false, Message = ex.Message, ElapsedMilliseconds = sw.ElapsedMilliseconds };
        }
    }

    private static void ApplyAuth(RestDataSource rest, HttpRequestMessage message)
    {
        switch (rest.AuthType)
        {
            case DataSourceAuthType.ApiKey when !string.IsNullOrWhiteSpace(rest.ApiKeyHeader):
                message.Headers.TryAddWithoutValidation(rest.ApiKeyHeader!, rest.ApiKeyValue ?? string.Empty);
                break;
            case DataSourceAuthType.Basic:
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{rest.Username}:{rest.Password}"));
                message.Headers.TryAddWithoutValidation("Authorization", $"Basic {token}");
                break;
            case DataSourceAuthType.Bearer:
                message.Headers.TryAddWithoutValidation("Authorization", $"Bearer {rest.BearerToken}");
                break;
        }
    }

    private static string BuildUrl(string baseUrl, string path, Dictionary<string, string> query, IDictionary<string, object?> parameters)
    {
        string url;
        if (string.IsNullOrEmpty(path))
            url = baseUrl;
        else if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = path;
        else
            url = $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

        if (query.Count > 0)
        {
            var qs = string.Join("&", query.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(TokenTemplate.Apply(kv.Value, parameters))}"));
            url += (url.Contains('?') ? "&" : "?") + qs;
        }
        return url;
    }
}
