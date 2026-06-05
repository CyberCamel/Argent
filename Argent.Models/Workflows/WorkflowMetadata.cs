using Argent.Models.Enums;
using Argent.Models.Identity;

namespace Argent.Models.Workflows;

public class WorkflowMetadata
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CreatedBy { get; set; }
    public string UpdatedBy { get; set; }
    public Version Version { get; set; }
    public WorkflowDefinitionState State { get; set; } = WorkflowDefinitionState.Draft;

}