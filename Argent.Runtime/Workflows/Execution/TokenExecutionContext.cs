using Argent.Core.Workflows.Execution;

namespace Argent.Runtime.Workflows.Execution;

public class TokenExecutionContext(
    Guid instanceId,
    Guid tokenId,
    Guid nodeId,
    IVariableBag variables,
    IReadOnlyList<CandidateTarget> candidateTargets,
    Guid? tokenGroupId,
    int? tokenCount,
    Guid recordId = default,
    Guid? formId = null,
    string objectKey = "") : ITokenExecutionContext
{
    public Guid InstanceId { get; } = instanceId;
    public Guid TokenId { get; } = tokenId;
    public Guid NodeId { get; } = nodeId;
    public Guid RecordId { get; } = recordId;
    public Guid? FormId { get; } = formId;
    public string ObjectKey { get; } = objectKey;
    public IVariableBag Variables { get; } = variables;
    public IReadOnlyList<CandidateTarget> CandidateTargets { get; } = candidateTargets;
    public Guid? TokenGroupId { get; } = tokenGroupId;
    public int? TokenCount { get; } = tokenCount;
}
