namespace Argent.Contracts.Workflows.Execution;

public interface ITokenExecutionContext
{
    Guid InstanceId { get; }
    Guid TokenId { get; }
    Guid NodeId { get; }
    IVariableBag Variables { get; }
    IReadOnlyList<CandidateTarget> CandidateTargets { get; }
    Guid? TokenGroupId { get; }
    int? TokenCount { get; }
}
