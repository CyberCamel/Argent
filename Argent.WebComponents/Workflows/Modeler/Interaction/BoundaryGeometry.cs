using Argent.Core.Workflows;
using Argent.Core.Workflows.BoundaryEvents;

namespace Argent.WebComponents.Workflows.Modeler.Interaction;

/// <summary>Placement rules for boundary events, shared by the toolbox, drag and resize.</summary>
public static class BoundaryGeometry
{
    /// <summary>Tolerance for treating a pointer as being on a node's edge.</summary>
    public const double PickTolerance = 24;

    /// <summary>
    /// The topmost non-boundary node containing or near (wx, wy), or null. Used both to pick
    /// a parent when placing a boundary event and to keep a detached one attached.
    /// </summary>
    public static DesignerNode? FindParentCandidate(
        IEnumerable<DesignerNode> nodes, double wx, double wy, DesignerNode? exclude = null) =>
        nodes.LastOrDefault(n =>
            n != exclude &&
            n.NodeData is not BoundaryEvent &&
            wx >= n.X - PickTolerance && wx <= n.X + n.Width + PickTolerance &&
            wy >= n.Y - PickTolerance && wy <= n.Y + n.Height + PickTolerance);

    /// <summary>
    /// Projects (cx, cy) onto the perimeter of <paramref name="parent"/> and returns the
    /// top-left position for a boundary event of the given size centred there.
    /// </summary>
    public static (double X, double Y) SnapToPerimeter(
        IDesignerItem parent, double cx, double cy, double width, double height)
    {
        double centerX = parent.X + parent.Width / 2.0;
        double centerY = parent.Y + parent.Height / 2.0;
        double radiusX = parent.Width / 2.0;
        double radiusY = parent.Height / 2.0;

        double relX = cx - centerX;
        double relY = cy - centerY;

        double edgeX, edgeY;
        if (Math.Abs(relX) < 0.001 && Math.Abs(relY) < 0.001)
        {
            edgeX = centerX;
            edgeY = centerY + radiusY;
        }
        else
        {
            // The smaller ratio reaches the perimeter first.
            double tx = Math.Abs(relX) > 0.001 ? radiusX / Math.Abs(relX) : double.MaxValue;
            double ty = Math.Abs(relY) > 0.001 ? radiusY / Math.Abs(relY) : double.MaxValue;
            double t = Math.Min(tx, ty);
            edgeX = centerX + relX * t;
            edgeY = centerY + relY * t;
        }

        return (edgeX - width / 2.0, edgeY - height / 2.0);
    }
}
