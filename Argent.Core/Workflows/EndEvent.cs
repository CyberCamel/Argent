using Argent.Core.Attributes;

namespace Argent.Core.Workflows;

[WorkflowCanvasElement("End Event", "stop", "End", NodeShape.Circle, "An event that ends a workflow", "workflow-node node-end", 50, 50)]
public class EndEvent : NodeBase
{
}
