using Argent.Models.Workflows.Execution;

namespace Argent.Contracts.Workflows.Execution;

public interface IUserTaskManager
{
    Task<UserTask> CreateTaskAsync(
        Guid instanceId,
        Guid tokenId,
        Guid nodeId,
        DateTime? dueDate,
        string? title = null,
        string? description = null,
        short priority = 0,
        Guid? formId = null,
        string? formData = null,
        CancellationToken ct = default);

    Task<UserTask?> GetTaskByTokenAsync(Guid tokenId, CancellationToken ct);

    Task<UserTask?> GetAsync(Guid taskId, CancellationToken ct);

    Task<List<UserTask>> GetTasksForUserAsync(
        string userId,
        List<string> roles,
        UserTaskState? stateFilter = null,
        CancellationToken ct = default);

    Task ClaimAsync(Guid taskId, string userId, CancellationToken ct = default);

    Task ReleaseAsync(Guid taskId, CancellationToken ct = default);

    Task ReassignAsync(Guid taskId, string toUser, CancellationToken ct = default);

    Task CompleteTaskAsync(Guid taskId, string completedBy, List<string> roles, string? action = null, CancellationToken ct = default);

    Task SetCandidateUsersAsync(Guid taskId, string candidateUsersJson, CancellationToken ct = default);
}
