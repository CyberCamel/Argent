using Argent.Core.Workflows.Execution;

namespace Argent.Core.Workflows.Execution;

public record InstanceSnapshot(
    Guid InstanceId,
    Guid WorkflowId,
    InstanceState State,
    int CurrentTokenCount,
    DateTime StartTime,
    DateTime? EndTime,
    Guid RecordId,
    IReadOnlyDictionary<string, Guid> RecordIds
);

public interface IWorkflowInstanceService
{
    Task<Guid> StartAsync(Guid definitionId, Guid recordId, IReadOnlyDictionary<string, object?>? variables, CancellationToken ct);
    Task<Guid> StartAsync(Guid definitionId, Guid recordId, IReadOnlyDictionary<string, Guid> recordIds, IReadOnlyDictionary<string, object?>? variables, CancellationToken ct);
    Task SaveRecordIdsAsync(Guid instanceId, IReadOnlyDictionary<string, Guid> recordIds, CancellationToken ct);
    Task SuspendAsync(Guid instanceId, CancellationToken ct);
    Task ResumeAsync(Guid instanceId, CancellationToken ct);
    Task CancelAsync(Guid instanceId, CancellationToken ct);
    Task<InstanceSnapshot> GetStateAsync(Guid instanceId, CancellationToken ct);
}
