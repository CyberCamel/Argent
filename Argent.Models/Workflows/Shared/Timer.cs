namespace Argent.Models.Workflows.Shared;

public class Timer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TokenId { get; set; }
    public Guid NodeId { get; set; }
    public string NodeType { get; set; } = string.Empty;
    public DateTime TriggerTime { get; set; }
    public TimerState State { get; set; } = TimerState.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
