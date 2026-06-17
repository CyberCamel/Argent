using Argent.Models.Attributes;
using Argent.Models.Workflows.Shared;

namespace Argent.Models.Workflows.Intermediates;

[WorkflowCanvasElement("Timer Event", "alarm", "Events", NodeShape.Circle,
    "Pauses the flow until a scheduled time", "node-timer", 80, 80)]
public class CatchingTimerEvent : CatchingIntermediateEvent
{
    public TimerDefinition Definition { get; set; } = new CronTimerDefinition();
}
