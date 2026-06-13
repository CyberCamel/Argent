using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows.Auditing;

public class WorkflowJournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstanceId { get; set; }
    public Guid? TokenId { get; set; }
    public WorkflowAuditEventType EventType { get; set; }
    public DateTime TimeStamp { get; set; } = DateTime.UtcNow;
    public string? Details { get; set; }
}
