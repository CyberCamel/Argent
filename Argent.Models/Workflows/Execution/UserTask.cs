namespace Argent.Models.Workflows.Execution;

public class UserTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstanceId { get; set; }
    public Guid TokenId { get; set; }
    public Guid NodeId { get; set; }
    public UserTaskState State { get; set; } = UserTaskState.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public string? ResultData { get; set; }
    public DateTime? DueDate { get; set; }
}
