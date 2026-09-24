using System.Text.Json;
using Argent.Core.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Core.Workflows.Auditing;
using Argent.Core.Workflows.Execution;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Workflows.Execution;

public class TaskInboxService : ITaskInboxService
{
    private readonly IDbContextFactory<ArgentDbContext> _contextFactory;
    private readonly IAuditService _audit;

    public TaskInboxService(IDbContextFactory<ArgentDbContext> contextFactory, IAuditService audit)
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
            details: new { Title = title, Priority = priority },
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
            .Where(t => IdentityEquals(t.AssignedTo, userId)
                     || CandidateUsersContains(t.CandidateUsers, userId, roles))
            .ToList();
    }

    public async Task<TaskListPage> QueryForUserAsync(
        string userId,
        List<string> roles,
        TaskListRequest request,
        CancellationToken ct = default)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var requestedPage = Math.Max(1, request.Page);
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var query = context.UserTasks.AsNoTracking();

        query = request.Scope == TaskListScope.Completed
            ? query.Where(task => task.State == UserTaskState.Completed)
            : query.Where(task => task.State == UserTaskState.Pending);

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(task =>
                (task.Title != null && task.Title.Contains(search)) ||
                (task.Description != null && task.Description.Contains(search)));

        query = request.Sort switch
        {
            TaskListSort.Newest => query.OrderByDescending(task => task.CreatedAt).ThenBy(task => task.Id),
            TaskListSort.Oldest => query.OrderBy(task => task.CreatedAt).ThenBy(task => task.Id),
            TaskListSort.Priority => query.OrderByDescending(task => task.Priority)
                .ThenBy(task => task.DueDate == null).ThenBy(task => task.DueDate).ThenBy(task => task.Id),
            _ => query.OrderBy(task => task.DueDate == null).ThenBy(task => task.DueDate)
                .ThenByDescending(task => task.Priority).ThenBy(task => task.Id)
        };

        // CandidateUsers also contains role names on tasks created by older workflow versions.
        // Apply the compatibility predicate before paging. Normalize candidates into a relation
        // when the authorization model is rebuilt so this can remain fully server-pushed.
        var accessible = (await query.ToListAsync(ct))
            .Where(task => IdentityEquals(task.AssignedTo, userId) ||
                (task.AssignedTo is null && CandidateUsersContains(task.CandidateUsers, userId, roles)))
            .ToList();
        var totalCount = accessible.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Min(requestedPage, totalPages);
        var tasks = accessible.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new TaskListPage(tasks.Select(task => new TaskListItem(
            task.Id,
            task.Title,
            task.Description,
            task.State,
            task.Priority,
            task.CreatedAt,
            task.DueDate,
            task.AssignedTo,
            task.State == UserTaskState.Pending && task.AssignedTo is null,
            task.State == UserTaskState.Pending && IdentityEquals(task.AssignedTo, userId),
            true)).ToList(), totalCount, page, pageSize);
    }

    public async Task SetCandidateUsersAsync(Guid taskId, string candidateUsersJson, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await context.UserTasks
            .Where(t => t.Id == taskId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CandidateUsers, candidateUsersJson), ct);
    }

    public async Task ClaimAsync(Guid taskId, string userId, List<string> roles, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = await context.UserTasks.FindAsync([taskId], ct);
        if (task == null)
            throw new InvalidOperationException($"UserTask {taskId} not found");

        if (task.State != UserTaskState.Pending)
            throw new InvalidOperationException("The task is no longer active.");

        if (task.AssignedTo != null)
            throw new InvalidOperationException("The task has already been claimed.");

        if (!CandidateUsersContains(task.CandidateUsers, userId, roles))
            throw new InvalidOperationException("The task is not available to this user.");

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

    public async Task ReleaseAsync(Guid taskId, string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var task = await context.UserTasks.FindAsync([taskId], ct);
        if (task == null)
            throw new InvalidOperationException($"UserTask {taskId} not found");

        if (task.State != UserTaskState.Pending)
            throw new InvalidOperationException("The task is no longer active.");

        if (!IdentityEquals(task.AssignedTo, userId))
            throw new InvalidOperationException("Only the current assignee can release this task.");

        var previousAssignee = task.AssignedTo;
        task.AssignedTo = null;
        task.ClaimedAt = null;
        await context.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            category: "Task",
            eventType: nameof(WorkflowAuditEventType.TaskReleased),
            instanceId: task.InstanceId,
            tokenId: task.TokenId,
            actor: userId,
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
        return IdentityEquals(task.AssignedTo, userId)
            || CandidateUsersContains(task.CandidateUsers, userId, roles);
    }

    private static bool CandidateUsersContains(string? raw, string userId, IEnumerable<string>? roles = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            var candidates = JsonSerializer.Deserialize<List<string>>(raw) ?? [];
            return candidates.Contains(userId, StringComparer.OrdinalIgnoreCase) ||
                (roles?.Any(role => candidates.Contains(role, StringComparer.OrdinalIgnoreCase)) ?? false);
        }
        catch
        {
            return false;
        }
    }

    private static bool IdentityEquals(string? left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
