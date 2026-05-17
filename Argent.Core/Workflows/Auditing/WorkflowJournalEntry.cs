using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows.Auditing;

public class WorkflowJournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkflowAuditEventType EventType { get; set; }
    public DateTime TimeStamp { get; set; } = DateTime.UtcNow;
    public object? Details { get; set; }


}
