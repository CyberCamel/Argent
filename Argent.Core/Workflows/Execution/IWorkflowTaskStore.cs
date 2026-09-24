namespace Argent.Core.Workflows.Execution;

public interface IWorkflowTaskStore
{
    /// <summary>Returns the FormId from the deployed workflow's start event, or null if not configured.</summary>
    Task<Guid?> GetStartFormIdAsync(Guid workflowId);

    /// <summary>Returns outgoing connection labels for a given node in the workflow instance (used as routing choices).</summary>
    Task<IReadOnlyList<string>> GetTaskActionsAsync(Guid instanceId, Guid nodeId);

    Task<IReadOnlyList<TaskActionDescriptor>> GetTaskActionDescriptorsAsync(Guid instanceId, Guid nodeId);
}
