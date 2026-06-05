using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows.Execution;

public record WorkItem
{
    public required Guid Id { get; init; }
    public required Guid WorkflowInstanceId { get; init; }
    public bool Locked { get; set; } = false;
    public string? LockedBy { get; set; } 
    public DateTime? LockExpirationUtc { get; set; }
    public byte RetryCount { get; set; }
}
