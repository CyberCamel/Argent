using Argent.Models.Attributes;

namespace Argent.Models.Workflows.Intermediates;

[WorkflowCanvasElement("CatchingTimerEvent", "clock", "Intermediates", NodeShape.Circle, "A timer", "workflow-node node-timer", 80, 80)]
public class CatchingTimerEvent
{
    public DateTime TriggerTime { get; set; }
    
}