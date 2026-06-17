using Argent.Models.Attributes;
using Argent.Models.Workflows.Shared;

namespace Argent.Models.Workflows.BoundaryEvents;

[WorkflowCanvasElement("Timer Boundary", "alarm", "Events", NodeShape.Circle,
    "Fires after a schedule while an activity is active", "node-boundary-timer", 48, 48)]
public class TimerBoundaryEvent : BoundaryEvent
{
    public TimerDefinition Definition { get; set; } = new CronTimerDefinition();
}
