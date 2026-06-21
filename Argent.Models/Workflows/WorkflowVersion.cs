using Argent.Models.Enums;

namespace Argent.Models.Workflows;

public class WorkflowVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;
    public Version Version { get; set; } = new(1, 0);
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WorkflowDefinition Definition { get; set; } = new();
    public WorkflowDefinitionState State { get; set; } = WorkflowDefinitionState.Draft;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    // Mutable post-publish. Key = ProcessRole.Id. Updated without touching the immutable Definition.
    public Dictionary<Guid, RoleAudience> RoleAudiences { get; set; } = [];
}
