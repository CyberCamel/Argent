using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Handlers;

public class ParallelGatewayEvaluatorTests
{
    private static ParallelGateway Node => new() { Id = Guid.NewGuid(), Name = "and" };

    [Fact]
    public async Task Activates_all_outgoing_paths()
    {
        var candidates = new List<CandidateTarget>
        {
            new(Guid.NewGuid(), "Task", null),
            new(Guid.NewGuid(), "Task", null),
            new(Guid.NewGuid(), "Task", null),
        };
        var bag = new TokenVariableBag();
        var ctx = new TokenExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Node.Id, bag, candidates, null, null);

        var result = await new ParallelGatewayEvaluator().ExecuteAsync(Node, ctx, default);

        Assert.True(result.Success);
        Assert.Equal(3, result.ExplicitTargetNodeIds!.Count);
        Assert.Contains(candidates[0].NodeId, result.ExplicitTargetNodeIds);
        Assert.Contains(candidates[1].NodeId, result.ExplicitTargetNodeIds);
        Assert.Contains(candidates[2].NodeId, result.ExplicitTargetNodeIds);
    }

    [Fact]
    public async Task Returns_failed_when_no_outgoing_connections()
    {
        var candidates = new List<CandidateTarget>();
        var bag = new TokenVariableBag();
        var ctx = new TokenExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Node.Id, bag, candidates, null, null);

        var result = await new ParallelGatewayEvaluator().ExecuteAsync(Node, ctx, default);

        Assert.False(result.Success);
        Assert.Equal("Parallel gateway has no outgoing connections", result.ErrorMessage);
    }
}
