using Argent.Models.Workflows.Execution;

namespace Argent.Contracts.Workflows.Execution;

public record InstanceSnapshot(
    Guid InstanceId,
    Guid WorkflowId,
    InstanceState State,
    int CurrentTokenCount,
    DateTime StartTime,
    DateTime? EndTime
);

public interface IWorkflowInstanceManager
{
    Task<Guid> StartAsync(Guid definitionId, IReadOnlyDictionary<string, object?>? variables, CancellationToken ct);
    Task SuspendAsync(Guid instanceId, CancellationToken ct);
    Task ResumeAsync(Guid instanceId, CancellationToken ct);
    Task CancelAsync(Guid instanceId, CancellationToken ct);
    Task<InstanceSnapshot> GetStateAsync(Guid instanceId, CancellationToken ct);
}
