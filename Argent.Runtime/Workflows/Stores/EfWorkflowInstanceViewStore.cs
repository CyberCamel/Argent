using Argent.Core.Workflows.Designer;
using Argent.Infrastructure.Data;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Execution;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Workflows.Stores;

public class EfWorkflowInstanceViewStore(IDbContextFactory<ArgentDbContext> _dbFactory) : IWorkflowInstanceViewStore
{
    public async Task<WorkflowInstanceViewDto?> LoadAsync(Guid instanceId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var instance = await db.WorkflowInstances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.InstanceId == instanceId);
        if (instance == null) return null;

        var version = await db.WorkflowVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == instance.VersionId);
        if (version == null) return null;

        var tokens = await db.WorkflowTokens.AsNoTracking()
            .Where(t => t.InstanceId == instanceId)
            .ToListAsync();

        var activeNodeIds = tokens
            .Where(t => t.State is TokenState.Ready or TokenState.Waiting)
            .Select(t => t.NodeId)
            .ToHashSet();

        var visitedNodeIds = tokens
            .Where(t => t.State == TokenState.Consumed)
            .Select(t => t.NodeId)
            .ToHashSet();

        return new WorkflowInstanceViewDto
        {
            Definition = version.Definition,
            ActiveNodeIds = activeNodeIds,
            VisitedNodeIds = visitedNodeIds
        };
    }
}
