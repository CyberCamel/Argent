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

    public async Task<UserTask> CreateTaskAsync(
        Guid instanceId,
        Guid tokenId,
        Guid nodeId,
        DateTime? dueDate,
        string? title = null,
        string? description = null,
        short priority = 0,
        string? assigneeExpression = null,
        string? candidateRoles = null,
        Guid? formId = null,
        string? formData = null,
        CancellationToken ct = default)
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
            CreatedAt = DateTime.UtcNow,
            Title = title,
            Description = description,
            Priority = priority,
            CandidateRoles = candidateRoles,
            FormId = formId,
            FormData = formData
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

    public async Task<UserTask?> GetAsync(Guid taskId, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.UserTasks.FindAsync([taskId], ct);
    }

    public async Task<List<UserTask>> GetTasksForUserAsync(
        string userId,
        List<string> roles,
        UserTaskState? stateFilter = null,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var query = context.UserTasks.AsQueryable();

        // Assigned to user OR candidate role matches (via JSON contains)
        query = query.Where(t =>
            t.AssignedTo == userId ||
            (t.CandidateRoles != null && roles.Any(r => t.CandidateRoles.Contains(r))));

        if (stateFilter.HasValue)
            query = query.Where(t => t.State == stateFilter.Value);

        return await query
            .OrderByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task ClaimAsync(Guid taskId, string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = await context.UserTasks.FindAsync([taskId], ct);
        if (task == null)
            throw new InvalidOperationException($"UserTask {taskId} not found");

        if (task.State != UserTaskState.Pending)
            return;

        task.AssignedTo = userId;
        task.ClaimedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    public async Task ReleaseAsync(Guid taskId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = await context.UserTasks.FindAsync([taskId], ct);
        if (task == null)
            throw new InvalidOperationException($"UserTask {taskId} not found");

        if (task.State != UserTaskState.Pending)
            return;

        task.AssignedTo = null;
        task.ClaimedAt = null;
        await context.SaveChangesAsync(ct);
    }

    public async Task ReassignAsync(Guid taskId, string toUser, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = await context.UserTasks.FindAsync([taskId], ct);
        if (task == null)
            throw new InvalidOperationException($"UserTask {taskId} not found");

        if (task.State != UserTaskState.Pending)
            return;

        task.AssignedTo = toUser;
        task.ClaimedAt = null;
        await context.SaveChangesAsync(ct);
    }

    public async Task CompleteTaskAsync(Guid taskId, string completedBy, string? resultData, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = await context.UserTasks.FindAsync([taskId], ct);
        if (task == null)
            throw new InvalidOperationException($"UserTask {taskId} not found");

        if (task.State != UserTaskState.Pending)
            return;

        task.State = UserTaskState.Completed;
        task.CompletedAt = DateTime.UtcNow;
        task.CompletedBy = completedBy;
        task.ResultData = resultData;

        var instance = await context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.InstanceId == task.InstanceId, ct);

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
