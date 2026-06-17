namespace Argent.Models.Workflows.BoundaryEvents;

public abstract class BoundaryEvent : NodeBase
{
    // References the activity node this event is attached to.
    public Guid ParentNodeId { get; set; }

    // When false, the parent activity continues running after the event fires.
    // When true, the parent activity is cancelled.
    public bool IsInterrupting { get; set; } = true;
}
