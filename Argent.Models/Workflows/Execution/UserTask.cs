using Argent.Models.Attributes;
using Argent.Models.Authorization;

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
    public string? AssignedTo { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public string? CandidateUsers { get; set; }
    public string? CandidateRoles { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public short Priority { get; set; }

    /// <summary>Optional form definition bound to this task.</summary>
    public Guid? FormId { get; set; }
    /// <summary>Seeded form values (pre-populated from workflow variables).</summary>
    public string? FormData { get; set; }

    /// <summary>Concurrency / optimistic-lock token.</summary>
    public Guid RowVersion { get; set; }
}
