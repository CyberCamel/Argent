using Argent.Models.Attributes;

namespace Argent.Models.Workflows;

[WorkflowCanvasElement("Start Event", "play_arrow", "Start", NodeShape.Circle, "An event that starts a workflow", "workflow-node node-start", 50, 50)]
public class StartEvent : NodeBase
{
    public string ObjectKey { get; set; } = string.Empty;
    public Guid? FormId { get; set; }
}
