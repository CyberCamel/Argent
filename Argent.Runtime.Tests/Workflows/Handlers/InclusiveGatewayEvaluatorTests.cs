using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Handlers;

public class InclusiveGatewayEvaluatorTests
{
    private static InclusiveGateway Node => new() { Id = Guid.NewGuid(), Name = "inclusive" };

    [Fact]
    public async Task Activates_single_matching_path()
    {
        var targets = new List<CandidateTarget>
        {
            new(Guid.NewGuid(), "Task", "[risk] = 'high'"),
            new(Guid.NewGuid(), "Task", "[risk] = 'medium'"),
            new(Guid.NewGuid(), "Task", "[risk] = 'low'"),
        };
        var bag = new TokenVariableBag(new Dictionary<string, object?> { ["risk"] = "medium" });
        var ctx = new TokenExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Node.Id, bag, targets, null, null);

        var result = await new InclusiveGatewayEvaluator().ExecuteAsync(Node, ctx, default);

        Assert.True(result.Success);
        Assert.Single(result.ExplicitTargetNodeIds!);
        Assert.Equal(targets[1].NodeId, result.ExplicitTargetNodeIds![0]);
    }

    [Fact]
    public async Task Activates_multiple_matching_paths()
    {
        var targets = new List<CandidateTarget>
        {
            new(Guid.NewGuid(), "Task", "[score] >= 80"),
            new(Guid.NewGuid(), "Task", "[score] >= 50"),
            new(Guid.NewGuid(), "Task", "[score] >= 0"),
        };
        var bag = new TokenVariableBag(new Dictionary<string, object?> { ["score"] = 75 });
        var ctx = new TokenExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Node.Id, bag, targets, null, null);

        var result = await new InclusiveGatewayEvaluator().ExecuteAsync(Node, ctx, default);

        Assert.True(result.Success);
        Assert.Equal(2, result.ExplicitTargetNodeIds!.Count);
        Assert.Contains(targets[1].NodeId, result.ExplicitTargetNodeIds);
        Assert.Contains(targets[2].NodeId, result.ExplicitTargetNodeIds);
    }

    [Fact]
    public async Task Falls_back_to_default_when_no_condition_matches()
    {
        var targets = new List<CandidateTarget>
        {
            new(Guid.NewGuid(), "Task", "[score] > 100"),
            new(Guid.NewGuid(), "Task", null), // default
        };
        var bag = new TokenVariableBag(new Dictionary<string, object?> { ["score"] = 50 });
        var ctx = new TokenExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Node.Id, bag, targets, null, null);

        var result = await new InclusiveGatewayEvaluator().ExecuteAsync(Node, ctx, default);

        Assert.True(result.Success);
        Assert.Single(result.ExplicitTargetNodeIds!);
        Assert.Equal(targets[1].NodeId, result.ExplicitTargetNodeIds![0]);
    }

    [Fact]
    public async Task Returns_failed_when_no_path_matches_and_no_default()
    {
        var targets = new List<CandidateTarget>
        {
            new(Guid.NewGuid(), "Task", "[score] > 100"),
        };
        var bag = new TokenVariableBag(new Dictionary<string, object?> { ["score"] = 50 });
        var ctx = new TokenExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Node.Id, bag, targets, null, null);

        var result = await new InclusiveGatewayEvaluator().ExecuteAsync(Node, ctx, default);

        Assert.False(result.Success);
        Assert.Equal("No matching path in inclusive gateway", result.ErrorMessage);
    }
}
