namespace Argent.Models.Workflows.Auditing;

public class WorkflowJournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Category { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public Guid InstanceId { get; set; }
    public Guid? TokenId { get; set; }
    public string? Actor { get; set; }
    public DateTime TimeStamp { get; set; } = DateTime.UtcNow;
    public string? Details { get; set; }
}
