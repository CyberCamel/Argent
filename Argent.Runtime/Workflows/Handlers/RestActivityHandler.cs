using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
using Argent.Runtime.DataSources;
using System.Text;
using System.Text.Json;

namespace Argent.Runtime.Workflows.Handlers;

public class RestActivityHandler : INodeHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public Type HandledNodeType => typeof(RestActivity);

    public RestActivityHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var activity = (RestActivity)node;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = activity.Timeout;

            var variables = ctx.Variables.Snapshot()
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var url = TokenTemplate.Apply(activity.Url, variables);

            var request = new HttpRequestMessage(
                new HttpMethod(activity.Method),
                url);

            foreach (var header in activity.Headers)
            {
                request.Headers.TryAddWithoutValidation(
                    TokenTemplate.Apply(header.Key, variables),
                    TokenTemplate.Apply(header.Value, variables));
            }

            if (activity.Body != null && (activity.Method == "POST" || activity.Method == "PUT" || activity.Method == "PATCH"))
            {
                var bodyString = activity.Body is string s
                    ? TokenTemplate.Apply(s, variables)
                    : JsonSerializer.Serialize(activity.Body);
                request.Content = new StringContent(bodyString, Encoding.UTF8, activity.ContentType);
            }

            using var response = await client.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            return new NodeResult(
                response.IsSuccessStatusCode,
                response.IsSuccessStatusCode ? null : $"HTTP {response.StatusCode}: {responseBody}",
                OutputVariables: new Dictionary<string, object?>
                {
                    ["statusCode"] = (int)response.StatusCode,
                    ["responseBody"] = responseBody,
                    ["responseHeaders"] = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value))
                });
        }
        catch (Exception ex)
        {
            return new NodeResult(false, ex.Message);
        }
    }
}
