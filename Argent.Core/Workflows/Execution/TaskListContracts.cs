namespace Argent.Core.Workflows.Execution;

public enum TaskListScope
{
    Active,
    Completed
}

public enum TaskListSort
{
    DueDate,
    Newest,
    Oldest,
    Priority
}

public sealed class TaskListRequest
{
    public string? Search { get; set; }
    public TaskListScope Scope { get; set; } = TaskListScope.Active;
    public TaskListSort Sort { get; set; } = TaskListSort.DueDate;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record TaskListItem(
    Guid Id,
    string? Title,
    string? Description,
    UserTaskState State,
    short Priority,
    DateTime CreatedAt,
    DateTime? DueDate,
    string? AssignedTo,
    bool CanClaim,
    bool CanRelease,
    bool CanOpen);

public sealed record TaskListPage(
    IReadOnlyList<TaskListItem> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
