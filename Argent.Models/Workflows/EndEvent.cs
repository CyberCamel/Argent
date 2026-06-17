using Argent.Models.Attributes;

namespace Argent.Models.Workflows;

[WorkflowCanvasElement("End Event", "stop", "End", NodeShape.Circle, "An event that ends a workflow", "workflow-node node-end", 50, 50)]
public class EndEvent : NodeBase
{
}
