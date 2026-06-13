using System.Diagnostics;
using System.Text;
using Argent.Contracts.DataSources;
using Argent.Models.DataSources;

namespace Argent.Runtime.DataSources;

public class SoapDataSourceProvider(IHttpClientFactory _httpClientFactory) : IDataSourceProvider
{
    public DataSourceKind Kind => DataSourceKind.Soap;

    public async Task<DataSourceResult> ExecuteAsync(
        DataSource dataSource, DataSourceRequest request, IDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        if (dataSource is not SoapDataSource soap) return DataSourceResult.Fail("Data source is not a SOAP connection.");
        if (request is not SoapRequest req) return DataSourceResult.Fail("Request is not a SOAP request.");

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(soap.TimeoutSeconds > 0 ? soap.TimeoutSeconds : 30);

            using var message = new HttpRequestMessage(HttpMethod.Post, soap.EndpointUrl)
            {
                Content = new StringContent(TokenTemplate.Apply(req.Envelope, parameters), Encoding.UTF8, "text/xml")
            };
            message.Headers.TryAddWithoutValidation("SOAPAction", $"\"{req.Action}\"");
            foreach (var (key, value) in soap.DefaultHeaders) message.Headers.TryAddWithoutValidation(key, value);
            foreach (var (key, value) in req.Headers) message.Headers.TryAddWithoutValidation(key, TokenTemplate.Apply(value, parameters));
            ApplyBasicAuth(soap, message);

            using var response = await client.SendAsync(message, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            // SOAP faults arrive as 500 with a fault body; surface as error but keep Raw for inspection.
            return new DataSourceResult
            {
                Raw = raw,
                Success = response.IsSuccessStatusCode,
                Error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
            };
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
            if (dataSource is not SoapDataSource soap)
                return new DataSourceTestResult { Success = false, Message = "Not a SOAP data source." };

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(soap.TimeoutSeconds > 0 ? soap.TimeoutSeconds : 30);
            using var message = new HttpRequestMessage(HttpMethod.Get, soap.EndpointUrl);
            ApplyBasicAuth(soap, message);

            using var response = await client.SendAsync(message, cancellationToken);
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

    private static void ApplyBasicAuth(SoapDataSource soap, HttpRequestMessage message)
    {
        if (soap.AuthType != DataSourceAuthType.Basic) return;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{soap.Username}:{soap.Password}"));
        message.Headers.TryAddWithoutValidation("Authorization", $"Basic {token}");
    }
}
