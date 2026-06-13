using Argent.Contracts.Workflows.Execution;

namespace Argent.Runtime.Workflows.Execution;

public class TokenExecutionContext(
    Guid instanceId,
    Guid tokenId,
    Guid nodeId,
    IVariableBag variables,
    IReadOnlyList<CandidateTarget> candidateTargets,
    Guid? tokenGroupId,
    int? tokenCount) : ITokenExecutionContext
{
    public Guid InstanceId { get; } = instanceId;
    public Guid TokenId { get; } = tokenId;
    public Guid NodeId { get; } = nodeId;
    public IVariableBag Variables { get; } = variables;
    public IReadOnlyList<CandidateTarget> CandidateTargets { get; } = candidateTargets;
    public Guid? TokenGroupId { get; } = tokenGroupId;
    public int? TokenCount { get; } = tokenCount;
}
