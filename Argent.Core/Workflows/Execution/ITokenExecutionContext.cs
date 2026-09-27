namespace Argent.Core.Workflows.Execution;

public interface ITokenExecutionContext
{
    Guid InstanceId { get; }
    Guid TokenId { get; }
    Guid NodeId { get; }

    /// <summary>The work item that is executing this node. Handlers that park the token record it so the result can release it.</summary>
    Guid WorkItemId { get; }

    Guid RecordId { get; }
    Guid? FormId { get; }
    string ObjectKey { get; }
    IVariableBag Variables { get; }
    IReadOnlyList<CandidateTarget> CandidateTargets { get; }
    Guid? TokenGroupId { get; }
    int? TokenCount { get; }
}
