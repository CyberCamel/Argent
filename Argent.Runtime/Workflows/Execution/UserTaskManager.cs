using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows.Execution;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Workflows.Execution;

public class UserTaskManager : IUserTaskManager
{
    private readonly IDbContextFactory<ArgentDbContext> _contextFactory;

    public UserTaskManager(IDbContextFactory<ArgentDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<UserTask> CreateTaskAsync(Guid instanceId, Guid tokenId, Guid nodeId, DateTime? dueDate, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = new UserTask
        {
            Id = Guid.NewGuid(),
            InstanceId = instanceId,
            TokenId = tokenId,
            NodeId = nodeId,
            State = UserTaskState.Pending,
            DueDate = dueDate,
            CreatedAt = DateTime.UtcNow
        };

        context.UserTasks.Add(task);
        await context.SaveChangesAsync(ct);

        return task;
    }

    public async Task<UserTask?> GetTaskByTokenAsync(Guid tokenId, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        return await context.UserTasks
            .Where(t => t.TokenId == tokenId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task CompleteTaskAsync(Guid taskId, string completedBy, string? resultData, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = await context.UserTasks.FindAsync([taskId], ct);
        if (task == null)
            throw new InvalidOperationException($"UserTask {taskId} not found");

        if (task.State != UserTaskState.Pending)
            return; // Already completed or cancelled — no-op

        task.State = UserTaskState.Completed;
        task.CompletedAt = DateTime.UtcNow;
        task.CompletedBy = completedBy;
        task.ResultData = resultData;

        // Look up the instance to get the workflow definition ID
        var instance = await context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.InstanceId == task.InstanceId, ct);

        // Create a new pending work item so the engine picks up the token
        context.WorkItems.Add(new WorkItem
        {
            Id = Guid.NewGuid(),
            TokenId = task.TokenId,
            WorkflowInstanceId = task.InstanceId,
            DefinitionId = instance?.WorkflowId ?? Guid.Empty,
            NodeId = task.NodeId,
            NodeType = "UserActivity",
            State = WorkItemState.Pending,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync(ct);
    }
}
