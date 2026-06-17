using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows.Execution;

public record WorkItem
{
    public required Guid Id { get; init; }
    public required Guid NodeId { get; init; }
    public required string NodeType { get; init; }
    public Guid TokenId { get; set; }
    public WorkItemState State { get; set; } = WorkItemState.Pending;
    public short Priority { get; set; } = 0;
    public DateTime? ScheduledAt { get; set; }
    public bool Locked { get; set; } = false;
    public string? LockedBy { get; set; }
    public DateTime? LockExpirationUtc { get; set; }
    public byte RetryCount { get; set; }
    public byte MaxRetries { get; set; } = 3;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
