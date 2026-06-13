using Argent.Models.Workflows;

namespace Argent.Contracts.Workflows.Execution;

public enum NodeResultType
{
    Completed,
    Waiting,
    Failed
}

public record NodeResult(
    bool Success,
    string? ErrorMessage = null,
    IReadOnlyDictionary<string, object?>? OutputVariables = null,
    IReadOnlyList<Guid>? ExplicitTargetNodeIds = null,
    NodeResultType ResultType = NodeResultType.Completed
);

public interface INodeHandler
{
    Type HandledNodeType { get; }
    Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct);
}
