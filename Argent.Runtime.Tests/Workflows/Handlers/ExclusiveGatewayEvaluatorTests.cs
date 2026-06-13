using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Handlers;

public class ExclusiveGatewayEvaluatorTests
{
    private static ExclusiveGateway Node => new() { Id = Guid.NewGuid(), Name = "xor" };

    [Fact]
    public async Task Selects_first_matching_condition()
    {
        var candidates = new List<CandidateTarget>
        {
            new(Guid.NewGuid(), "Task", "[amount] > 100"),
            new(Guid.NewGuid(), "Task", "[amount] <= 100"),
        };
        var bag = new TokenVariableBag(new Dictionary<string, object?> { ["amount"] = 200 });
        var ctx = new TokenExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Node.Id, bag, candidates, null, null);

        var result = await new ExclusiveGatewayEvaluator().ExecuteAsync(Node, ctx, default);

        Assert.True(result.Success);
        Assert.Single(result.ExplicitTargetNodeIds!);
        Assert.Equal(candidates[0].NodeId, result.ExplicitTargetNodeIds![0]);
    }

    [Fact]
    public async Task Selects_default_path_when_no_condition_matches()
    {
        var candidates = new List<CandidateTarget>
        {
            new(Guid.NewGuid(), "Task", "[amount] > 200"),
            new(Guid.NewGuid(), "Task", null), // default
        };
        var bag = new TokenVariableBag(new Dictionary<string, object?> { ["amount"] = 100 });
        var ctx = new TokenExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Node.Id, bag, candidates, null, null);

        var result = await new ExclusiveGatewayEvaluator().ExecuteAsync(Node, ctx, default);

        Assert.True(result.Success);
        Assert.Single(result.ExplicitTargetNodeIds!);
        Assert.Equal(candidates[1].NodeId, result.ExplicitTargetNodeIds![0]); // default path
    }

    [Fact]
    public async Task Returns_failed_when_no_path_matches_and_no_default()
    {
        var candidates = new List<CandidateTarget>
        {
            new(Guid.NewGuid(), "Task", "[amount] > 200"),
        };
        var bag = new TokenVariableBag(new Dictionary<string, object?> { ["amount"] = 100 });
        var ctx = new TokenExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Node.Id, bag, candidates, null, null);

        var result = await new ExclusiveGatewayEvaluator().ExecuteAsync(Node, ctx, default);

        Assert.False(result.Success);
        Assert.Equal("No matching path in exclusive gateway", result.ErrorMessage);
    }

    [Fact]
    public async Task Selects_first_default_path_on_no_expressions()
    {
        var candidates = new List<CandidateTarget>
        {
            new(Guid.NewGuid(), "Task", null),
        };
        var bag = new TokenVariableBag();
        var ctx = new TokenExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Node.Id, bag, candidates, null, null);

        var result = await new ExclusiveGatewayEvaluator().ExecuteAsync(Node, ctx, default);

        Assert.True(result.Success);
        Assert.Single(result.ExplicitTargetNodeIds!);
        Assert.Equal(candidates[0].NodeId, result.ExplicitTargetNodeIds![0]);
    }
}
