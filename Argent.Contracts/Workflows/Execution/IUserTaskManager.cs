using Argent.Models.Workflows.Execution;

namespace Argent.Contracts.Workflows.Execution;

public interface IUserTaskManager
{
    Task<UserTask> CreateTaskAsync(Guid instanceId, Guid tokenId, Guid nodeId, DateTime? dueDate, CancellationToken ct);
    Task<UserTask?> GetTaskByTokenAsync(Guid tokenId, CancellationToken ct);
    Task CompleteTaskAsync(Guid taskId, string completedBy, string? resultData, CancellationToken ct);
}
