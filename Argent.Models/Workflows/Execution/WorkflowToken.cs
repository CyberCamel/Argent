using Argent.Models.Workflows;

namespace Argent.Models.Workflows.Execution;

public class WorkflowToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstanceId { get; set; }
    public Guid NodeId { get; set; }
    public TokenState State { get; set; } = TokenState.Ready;
    public string? Payload { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConsumedAt { get; set; }
}
