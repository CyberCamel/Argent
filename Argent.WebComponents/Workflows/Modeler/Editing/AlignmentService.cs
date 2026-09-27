using Argent.WebComponents.Workflows.Modeler.Interaction;

namespace Argent.WebComponents.Workflows.Modeler.Editing;

public enum AlignMode
{
    Left,
    CenterHorizontal,
    Right,
    Top,
    CenterVertical,
    Bottom
}

public enum DistributeMode
{
    Horizontal,
    Vertical
}

/// <summary>
/// Alignment and distribution for a selection. Every operation moves nodes relative to the
/// gesture baseline, so it is a single undoable transaction like any other gesture.
/// </summary>
public static class AlignmentService
{
    public static bool Align(DesignerService state, DesignerSnapshot baseline, AlignMode mode)
    {
        var nodes = state.SelectedNodes.ToList();
        if (nodes.Count < 2) return false;

        double minX = nodes.Min(n => n.X);
        double maxX = nodes.Max(n => n.X + n.Width);
        double minY = nodes.Min(n => n.Y);
        double maxY = nodes.Max(n => n.Y + n.Height);

        foreach (var node in nodes)
        {
            double x = baseline.OriginOf(node).X;
            double y = baseline.OriginOf(node).Y;
            double w = node.Width, h = node.Height;

            switch (mode)
            {
                case AlignMode.Left: x = minX; break;
                case AlignMode.CenterHorizontal: x = (minX + maxX - w) / 2; break;
                case AlignMode.Right: x = maxX - w; break;
                case AlignMode.Top: y = minY; break;
                case AlignMode.CenterVertical: y = (minY + maxY - h) / 2; break;
                case AlignMode.Bottom: y = maxY - h; break;
            }

            node.X = x;
            node.Y = y;
        }

        GeometryEditor.For(state).RefreshRoutes(baseline, nodes);
        return true;
    }

    public static bool Distribute(DesignerService state, DesignerSnapshot baseline, DistributeMode mode)
    {
        var nodes = state.SelectedNodes.ToList();
        if (nodes.Count < 3) return false;

        bool horizontal = mode == DistributeMode.Horizontal;
        var ordered = nodes
            .OrderBy(n => horizontal ? n.X : n.Y)
            .ToList();

        double first = horizontal ? ordered[0].X : ordered[0].Y;
        var last = ordered[^1];
        double lastEdge = horizontal ? last.X + last.Width : last.Y + last.Height;
        double totalSize = nodes.Sum(n => horizontal ? n.Width : n.Height);
        double span = (horizontal ? lastEdge - first : lastEdge - first) - totalSize;
        double gap = span / (ordered.Count - 1);

        double cursor = first;
        foreach (var node in ordered)
        {
            if (horizontal) node.X = cursor; else node.Y = cursor;
            cursor += (horizontal ? node.Width : node.Height) + gap;
        }

        GeometryEditor.For(state).RefreshRoutes(baseline, nodes);
        return true;
    }
}
