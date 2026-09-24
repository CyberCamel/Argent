using Argent.Core.Workflows;

namespace Argent.Core.Workflows.Designer;

public interface IWorkflowInstanceViewStore
{
    Task<WorkflowInstanceViewDto?> LoadAsync(Guid instanceId);
}

public class WorkflowInstanceViewDto
{
    public required WorkflowDefinition Definition { get; init; }
    public HashSet<Guid> VisitedNodeIds { get; init; } = [];
    public HashSet<Guid> ActiveNodeIds { get; init; } = [];
}
