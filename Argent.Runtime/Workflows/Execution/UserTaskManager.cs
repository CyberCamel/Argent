using System.Text.Json;
using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows.Auditing;
using Argent.Models.Workflows.Execution;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Workflows.Execution;

public class UserTaskManager : IUserTaskManager
{
    private readonly IDbContextFactory<ArgentDbContext> _contextFactory;
    private readonly IAuditService _audit;

    public UserTaskManager(IDbContextFactory<ArgentDbContext> contextFactory, IAuditService audit)
    {
        _contextFactory = contextFactory;
        _audit = audit;
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
            FormId = formId,
            FormData = formData
        };

        context.UserTasks.Add(task);
        await context.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            category: "Task",
            eventType: nameof(WorkflowAuditEventType.TaskCreated),
            instanceId: instanceId,
            tokenId: tokenId,
            details: new { Title = title, AssigneeExpression = assigneeExpression, Priority = priority },
            ct: ct);

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

        if (stateFilter.HasValue)
            query = query.Where(t => t.State == stateFilter.Value);

        var tasks = await query
            .OrderByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return tasks
            .Where(t => t.AssignedTo == userId
                     || CandidateUsersContains(t.CandidateUsers, userId))
            .ToList();
    }

    public async Task SetCandidateUsersAsync(Guid taskId, string candidateUsersJson, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await context.UserTasks
            .Where(t => t.Id == taskId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CandidateUsers, candidateUsersJson), ct);
    }

    public async Task ClaimAsync(Guid taskId, string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = await context.UserTasks.FindAsync([taskId], ct);
        if (task == null)
            throw new InvalidOperationException($"UserTask {taskId} not found");

        if (task.State != UserTaskState.Pending)
            return;

        if (task.AssignedTo != null)
            return;

        task.AssignedTo = userId;
        task.ClaimedAt = DateTime.UtcNow;
        task.RowVersion = Guid.NewGuid();
        await context.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            category: "Task",
            eventType: nameof(WorkflowAuditEventType.TaskStarted),
            instanceId: task.InstanceId,
            tokenId: task.TokenId,
            actor: userId,
            details: new { TaskId = taskId, Title = task.Title },
            ct: ct);
    }

    public async Task ReleaseAsync(Guid taskId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = await context.UserTasks.FindAsync([taskId], ct);
        if (task == null)
            throw new InvalidOperationException($"UserTask {taskId} not found");

        if (task.State != UserTaskState.Pending)
            return;

        var previousAssignee = task.AssignedTo;
        task.AssignedTo = null;
        task.ClaimedAt = null;
        await context.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            category: "Task",
            eventType: nameof(WorkflowAuditEventType.TaskReleased),
            instanceId: task.InstanceId,
            tokenId: task.TokenId,
            actor: previousAssignee,
            details: new { TaskId = taskId, Title = task.Title },
            ct: ct);
    }

    public async Task ReassignAsync(Guid taskId, string toUser, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = await context.UserTasks.FindAsync([taskId], ct);
        if (task == null)
            throw new InvalidOperationException($"UserTask {taskId} not found");

        if (task.State != UserTaskState.Pending)
            return;

        var fromUser = task.AssignedTo;
        task.AssignedTo = toUser;
        task.ClaimedAt = null;
        await context.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            category: "Task",
            eventType: nameof(WorkflowAuditEventType.TaskReassigned),
            instanceId: task.InstanceId,
            tokenId: task.TokenId,
            details: new { TaskId = taskId, Title = task.Title, From = fromUser, To = toUser },
            ct: ct);
    }

    public async Task CompleteTaskAsync(Guid taskId, string completedBy, List<string> roles, string? action = null, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = await context.UserTasks.FindAsync([taskId], ct);
        if (task == null)
            throw new InvalidOperationException($"UserTask {taskId} not found");

        if (task.State != UserTaskState.Pending)
            return;

        if (!IsAuthorizedForTask(task, completedBy, roles))
            throw new InvalidOperationException($"User {completedBy} is not authorized to complete task {taskId}");

        task.State = UserTaskState.Completed;
        task.CompletedAt = DateTime.UtcNow;
        task.CompletedBy = completedBy;
        task.RowVersion = Guid.NewGuid();
        if (!string.IsNullOrEmpty(action))
            task.ResultData = action;

        await context.WorkItems
            .Where(w => w.TokenId == task.TokenId && w.State == WorkItemState.Waiting)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.State, WorkItemState.Pending)
                .SetProperty(w => w.LockedBy, (string?)null)
                .SetProperty(w => w.LockExpirationUtc, (DateTime?)null), ct);

        await context.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            category: "Task",
            eventType: nameof(WorkflowAuditEventType.TaskCompleted),
            instanceId: task.InstanceId,
            tokenId: task.TokenId,
            actor: completedBy,
            details: new { TaskId = taskId, Title = task.Title },
            ct: ct);
    }

    private static bool IsAuthorizedForTask(UserTask task, string userId, List<string> roles)
    {
        return task.AssignedTo == userId
            || CandidateUsersContains(task.CandidateUsers, userId);
    }

    private static bool CandidateUsersContains(string? raw, string userId)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw)?.Contains(userId) ?? false;
        }
        catch
        {
            return false;
        }
    }
}
