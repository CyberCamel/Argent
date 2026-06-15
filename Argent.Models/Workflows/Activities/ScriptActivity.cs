using Argent.Models.Attributes;

namespace Argent.Models.Workflows.Activities;

[WorkflowCanvasElement("Script Task", "bolt", "Server", NodeShape.Rectangle, "A task that executes a sequence of configurable actions", "workflow-node node-script")]
public class ScriptActivity : ServerActivity
{
    public List<ScriptAction> Actions { get; set; } = [];
}
