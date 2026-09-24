using Argent.Core.Attributes;
using Argent.Core.Workflows.Shared;

namespace Argent.Core.Workflows.BoundaryEvents;

[WorkflowCanvasElement("Timer Boundary", "alarm", "Events", NodeShape.Circle,
    "Fires after a schedule while an activity is active", "node-boundary-timer", 48, 48)]
public class TimerBoundaryEvent : BoundaryEvent
{
    public TimerDefinition Definition { get; set; } = new CronTimerDefinition();
}
