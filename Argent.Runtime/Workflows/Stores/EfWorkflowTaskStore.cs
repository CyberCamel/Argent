using Argent.Core.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Core.Enums;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Execution;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Workflows.Stores;

public class EfWorkflowTaskStore(IDbContextFactory<ArgentDbContext> _dbFactory) : IWorkflowTaskStore
{
    public async Task<Guid?> GetStartFormIdAsync(Guid workflowId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var version = await db.WorkflowVersions.AsNoTracking()
            .Where(v => v.WorkflowId == workflowId && v.State == WorkflowDefinitionState.Deployed)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        if (version == null) return null;

        var startEvent = version.Definition.Nodes.OfType<StartEvent>().FirstOrDefault();
        return startEvent?.FormId;
    }

    public async Task<IReadOnlyList<string>> GetTaskActionsAsync(Guid instanceId, Guid nodeId)
        => (await GetTaskActionDescriptorsAsync(instanceId, nodeId)).Select(action => action.Key).ToList();

    public async Task<IReadOnlyList<TaskActionDescriptor>> GetTaskActionDescriptorsAsync(Guid instanceId, Guid nodeId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var instance = await db.WorkflowInstances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.InstanceId == instanceId);
        if (instance == null) return [];

        var version = await db.WorkflowVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == instance.VersionId);
        if (version == null) return [];

        return TaskActionDescriptors.FromConnections(version.Definition.Connections, nodeId);
    }
}
