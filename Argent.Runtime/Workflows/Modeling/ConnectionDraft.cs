using Argent.Contracts.Workflows;

namespace Argent.Runtime.Workflows.Modeling;

public class ConnectionDraft
{
    /// <summary>The end that stays attached while the other end follows the mouse.</summary>
    public required DesignerNode FixedNode { get; set; }
    public AnchorDirection FixedDir { get; set; }

    /// <summary>
    /// True when the source endpoint is being re-dragged: the fixed node keeps its
    /// target role, and the node the draft is dropped on becomes the new source.
    /// </summary>
    public bool FixedEndIsTarget { get; set; }

    public double MouseX { get; set; }
    public double MouseY { get; set; }
    public IDesignerItem? TargetHint { get; set; }
}
