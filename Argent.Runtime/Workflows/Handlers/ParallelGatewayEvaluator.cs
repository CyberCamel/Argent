using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;

namespace Argent.Runtime.Workflows.Handlers;

public class ParallelGatewayEvaluator : INodeHandler
{
    public Type HandledNodeType => typeof(ParallelGateway);

    public Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        // ParallelGateway: activate ALL outgoing paths — no conditions to evaluate
        var targets = ctx.CandidateTargets
            .Select(t => t.NodeId)
            .ToList();

        return Task.FromResult(new NodeResult(
            targets.Count > 0,
            targets.Count == 0 ? "Parallel gateway has no outgoing connections" : null,
            ExplicitTargetNodeIds: targets,
            ResultType: targets.Count == 0 ? NodeResultType.Failed : NodeResultType.Completed));
    }
}
