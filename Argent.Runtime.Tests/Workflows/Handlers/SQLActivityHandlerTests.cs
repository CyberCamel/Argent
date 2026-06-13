using Argent.Contracts.DataSources;
using Argent.Contracts.Workflows.Execution;
using Argent.Models.DataSources;
using Argent.Models.Workflows.Activities;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Handlers;

public class SQLActivityHandlerTests
{
    private static TokenExecutionContext Context(Dictionary<string, object?> vars) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new TokenVariableBag(vars), [], null, null);

    [Fact]
    public async Task Executes_query_and_returns_rows()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["id"] = 1 },
            new() { ["id"] = 2 },
        };

        var runner = new Mock<IDataSourceRunner>();
        runner
            .Setup(r => r.ExecuteAsync("orders-db", It.IsAny<DataSourceRequest>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataSourceResult { Success = true, Rows = rows });

        var activity = new SQLActivity
        {
            Id = Guid.NewGuid(),
            Name = "Load",
            ConnectionKey = "orders-db",
            Query = "SELECT id FROM Orders",
            Parameters = new Dictionary<string, string> { ["custId"] = "customer" },
        };

        var result = await new SQLActivityHandler(runner.Object)
            .ExecuteAsync(activity, Context(new() { ["customer"] = 42 }), default);

        Assert.True(result.Success);
        Assert.Equal(2, result.OutputVariables!["rowCount"]);
        Assert.Same(rows, result.OutputVariables["result"]);

        runner.Verify(r => r.ExecuteAsync(
            "orders-db",
            It.Is<SqlRequest>(s => s.Query == "SELECT id FROM Orders"),
            It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Failure_in_runner_returns_failed_result()
    {
        var runner = new Mock<IDataSourceRunner>();
        runner
            .Setup(r => r.ExecuteAsync(It.IsAny<string>(), It.IsAny<DataSourceRequest>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection refused"));

        var activity = new SQLActivity { Id = Guid.NewGuid(), Name = "Load", ConnectionKey = "db", Query = "SELECT 1" };

        var result = await new SQLActivityHandler(runner.Object)
            .ExecuteAsync(activity, Context([]), default);

        Assert.False(result.Success);
        Assert.Equal("connection refused", result.ErrorMessage);
    }
}
