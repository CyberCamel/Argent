using System.Net;
using Argent.Models.Workflows.Activities;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Handlers;

public class RestActivityHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public CapturingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body),
            });
        }
    }

    private static TokenExecutionContext Context(Dictionary<string, object?> vars) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new TokenVariableBag(vars), [], null, null);

    private static IHttpClientFactory Factory(CapturingHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler));
        return factory.Object;
    }

    [Fact]
    public async Task Substitutes_tokens_in_url_and_headers()
    {
        var capturing = new CapturingHandler(HttpStatusCode.OK, "{\"ok\":true}");

        var activity = new RestActivity
        {
            Id = Guid.NewGuid(),
            Name = "Call",
            Url = "https://api.test/orders/{{orderId}}",
            Method = "GET",
            Headers = new Dictionary<string, string> { ["X-Trace"] = "{{orderId}}" },
        };

        var result = await new RestActivityHandler(Factory(capturing))
            .ExecuteAsync(activity, Context(new() { ["orderId"] = "A-7" }), default);

        Assert.True(result.Success);
        Assert.Equal("https://api.test/orders/A-7", capturing.Last!.RequestUri!.ToString());
        Assert.Equal("A-7", capturing.Last.Headers.GetValues("X-Trace").Single());
        Assert.Equal(200, result.OutputVariables!["statusCode"]);
        Assert.Equal("{\"ok\":true}", result.OutputVariables["responseBody"]);
    }

    [Fact]
    public async Task Non_success_status_returns_failed_result()
    {
        var capturing = new CapturingHandler(HttpStatusCode.InternalServerError, "boom");

        var activity = new RestActivity
        {
            Id = Guid.NewGuid(),
            Name = "Call",
            Url = "https://api.test/x",
            Method = "GET",
        };

        var result = await new RestActivityHandler(Factory(capturing))
            .ExecuteAsync(activity, Context([]), default);

        Assert.False(result.Success);
        Assert.Equal(500, result.OutputVariables!["statusCode"]);
    }
}
