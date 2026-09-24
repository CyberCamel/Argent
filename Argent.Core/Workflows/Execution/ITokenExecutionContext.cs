namespace Argent.Core.Workflows.Execution;

public interface ITokenExecutionContext
{
    Guid InstanceId { get; }
    Guid TokenId { get; }
    Guid NodeId { get; }
    Guid RecordId { get; }
    Guid? FormId { get; }
    string ObjectKey { get; }
    IVariableBag Variables { get; }
    IReadOnlyList<CandidateTarget> CandidateTargets { get; }
    Guid? TokenGroupId { get; }
    int? TokenCount { get; }
}
