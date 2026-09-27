using Argent.Core.Workflows;
using Argent.Core.Workflows.BoundaryEvents;
using Argent.WebComponents.Workflows.Modeler.Routing;

namespace Argent.WebComponents.Workflows.Modeler.Interaction;

/// <summary>
/// Every geometry-changing gesture in the modeler, expressed against the snapshot captured
/// when the gesture began. Node drags, group drags, pool drags, resizes and the space tool
/// all funnel through here, so they share one set of rules: movement is computed from the
/// gesture baseline rather than from accumulated deltas, attached boundary events follow
/// their parent exactly once, and only connections that can actually be affected are
/// rerouted.
/// </summary>
public sealed class GeometryEditor(DesignerService state)
{
    /// <summary>How far a moved node must come within a connector's bounding box to force a reroute.</summary>
    private const double CorridorMargin = 24;

    private const double LaneBorderZone = 10;
    private const double PoolHeaderWidth = 40;
    private const double LaneHeaderWidth = 40;
    public const double PoolMinWidth = 160;
    public const double PoolMinHeight = 80;
    public const double LaneMinHeight = 40;
    public const double NodeMinWidth = 80;
    public const double NodeMinHeight = 50;

    public DesignerService State { get; } = state;

    public static GeometryEditor For(DesignerService state) => new(state);

    /// <summary>Translates a set of nodes, keeping attached boundary events with their parents.</summary>
    public void MoveNodes(DesignerSnapshot baseline, IEnumerable<DesignerNode> nodes, double dx, double dy)
    {
        var moving = new HashSet<DesignerNode>(nodes);
        foreach (var child in AttachedBoundaryEvents(moving))
            moving.Add(child);

        foreach (var node in moving)
        {
            var (originX, originY) = baseline.OriginOf(node);
            if (IsDetached(node, moving))
            {
                var parent = ParentOf(node);
                if (parent != null)
                {
                    double cx = originX + dx + node.Width / 2;
                    double cy = originY + dy + node.Height / 2;
                    var (snappedX, snappedY) = BoundaryGeometry.SnapToPerimeter(
                        parent, cx, cy, node.Width, node.Height);
                    node.X = snappedX;
                    node.Y = snappedY;
                    continue;
                }
            }

            double x = originX + dx;
            double y = originY + dy;
            var pool = PoolAt(x + node.Width / 2, y + node.Height / 2);
            if (pool != null) (x, y) = ClampToPoolContent(pool, x, y, node.Width, node.Height);
            node.X = x;
            node.Y = y;
        }

        RefreshRoutes(baseline, moving);
    }

    /// <summary>
    /// Moves a pool to an absolute position, along with its lanes and every node the pool
    /// contains. The position is measured from the gesture baseline, so repeated pointer
    /// positions cannot accumulate drift.
    /// </summary>
    public void MovePool(DesignerSnapshot baseline, DesignerPool pool, double targetX, double targetY)
    {
        var (originX, originY, _, _) = baseline.PoolOriginOf(pool);
        double dx = targetX - originX;
        double dy = targetY - originY;

        pool.X = targetX;
        pool.Y = targetY;
        foreach (var lane in State.Lanes.Where(l => l.Pool == pool))
        {
            var origin = baseline.LaneOriginOf(lane);
            lane.X = origin.X + dx;
            lane.Y = origin.Y + dy;
            lane.Width = origin.Width;
            lane.Height = origin.Height;
        }

        var moved = new HashSet<DesignerNode>(State.Nodes.Where(n => IsNodeInPool(n, pool)));
        foreach (var node in moved)
        {
            var (nodeX, nodeY) = baseline.OriginOf(node);
            node.X = nodeX + dx;
            node.Y = nodeY + dy;
        }

        RefreshRoutes(baseline, moved);
    }

    /// <summary>Resizes a node, keeping its boundary events on the new perimeter.</summary>
    public void ResizeNode(DesignerNode node, ResizeEdges edges, double dx, double dy)
    {
        double width = node.Width, height = node.Height;
        double x = node.X, y = node.Y;

        if (edges.Right) width = Math.Max(NodeMinWidth, node.Width + dx);
        if (edges.Bottom) height = Math.Max(NodeMinHeight, node.Height + dy);
        if (edges.Left)
        {
            double candidate = Math.Max(NodeMinWidth, node.Width - dx);
            if (candidate > NodeMinWidth || dx < 0)
            {
                x = node.X + (node.Width - candidate);
                width = candidate;
            }
        }
        if (edges.Top)
        {
            double candidate = Math.Max(NodeMinHeight, node.Height - dy);
            if (candidate > NodeMinHeight || dy < 0)
            {
                y = node.Y + (node.Height - candidate);
                height = candidate;
            }
        }

        node.X = x;
        node.Y = y;
        node.Width = width;
        node.Height = height;

        foreach (var child in AttachedBoundaryEvents([node]))
        {
            double cx = child.X + child.Width / 2;
            double cy = child.Y + child.Height / 2;
            var (snappedX, snappedY) = BoundaryGeometry.SnapToPerimeter(node, cx, cy, child.Width, child.Height);
            child.X = snappedX;
            child.Y = snappedY;
        }
    }

    public void ResizePool(DesignerPool pool, bool resizeWidth, bool resizeHeight, double dx, double dy)
    {
        if (resizeWidth)
        {
            double width = Math.Max(PoolMinWidth, pool.Width + dx);
            foreach (var lane in state.Lanes.Where(l => l.Pool == pool))
                lane.Width = width - LaneHeaderWidth;
            pool.Width = width;
        }
        if (resizeHeight)
        {
            double height = Math.Max(PoolMinHeight, pool.Height + dy);
            double change = height - pool.Height;
            pool.Height = height;
            var lastLane = state.Lanes
                .Where(l => l.Pool == pool)
                .OrderByDescending(l => l.Data.Order)
                .FirstOrDefault();
            if (lastLane != null)
                lastLane.Height = Math.Max(LaneMinHeight, lastLane.Height + change);
        }
    }

    /// <summary>
    /// Places the divider between two lanes at an absolute y. Neither lane can be pushed
    /// below its minimum, so compression stops instead of overlapping.
    /// </summary>
    public void ResizeLaneDivider(DesignerLane top, DesignerLane bottom, double dividerY)
    {
        double available = bottom.Y + bottom.Height - top.Y;
        double topHeight = Math.Clamp(dividerY - top.Y,
            LaneMinHeight, Math.Max(LaneMinHeight, available - LaneMinHeight));
        double bottomHeight = Math.Max(LaneMinHeight, available - topHeight);

        top.Height = topHeight;
        bottom.Y = top.Y + topHeight;
        bottom.Height = bottomHeight;
    }

    /// <summary>
    /// Applies the space tool cut at a single gesture position. Pools, lanes and nodes on
    /// the far side of the cut move or compress; compression stops at the minimum sizes
    /// instead of overlapping.
    /// </summary>
    public void ApplySpaceTool(
        DesignerSnapshot baseline, double cutX, double cutY, bool isHorizontal, double delta)
    {
        foreach (var pool in state.Pools)
        {
            var (originX, originY, originW, originH) = baseline.PoolOriginOf(pool);
            if (isHorizontal)
            {
                pool.X = originX;
                pool.Y = originY;
                pool.Width = originW;
                pool.Height = originH;
                if (cutX <= originX) pool.X += delta;
                else if (cutX < originX + originW)
                    pool.Width = Math.Max(PoolMinWidth, originW + delta);
            }
            else
            {
                pool.X = originX;
                pool.Y = originY;
                pool.Width = originW;
                pool.Height = originH;
                if (cutY <= originY) pool.Y += delta;
                else if (cutY < originY + originH)
                    pool.Height = Math.Max(PoolMinHeight, originH + delta);
            }
        }

        foreach (var lane in state.Lanes)
        {
            var origin = baseline.LaneOriginOf(lane);
            lane.X = origin.X;
            lane.Y = origin.Y;
            lane.Width = origin.Width;
            lane.Height = origin.Height;
            if (isHorizontal)
            {
                if (cutX <= origin.X) lane.X += delta;
                else if (cutX < origin.X + origin.Width)
                    lane.Width = Math.Max(PoolMinWidth, origin.Width + delta);
            }
            else
            {
                if (cutY <= origin.Y) lane.Y += delta;
                else if (cutY < origin.Y + origin.Height)
                    lane.Height = Math.Max(LaneMinHeight, origin.Height + delta);
            }
        }

        var moved = new HashSet<DesignerNode>();
        foreach (var node in state.Nodes)
        {
            var (originX, originY) = baseline.OriginOf(node);
            var (parentX, parentY) = node.NodeData is BoundaryEvent boundary
                ? OriginOfParent(baseline, boundary)
                : (originX, originY);

            double dx = isHorizontal && parentX > cutX ? delta : 0;
            double dy = !isHorizontal && parentY > cutY ? delta : 0;
            node.X = originX + dx;
            node.Y = originY + dy;
            if (dx != 0 || dy != 0) moved.Add(node);
        }

        RefreshRoutes(baseline, moved);
    }

    /// <summary>
    /// Reroutes the connections a set of moved nodes can have affected. Connections with
    /// both ends moving are translated as a unit, connections with one moving end are
    /// recomputed, and connections that are merely nearby are left alone unless a moved
    /// node actually entered their corridor.
    /// </summary>
    public void RefreshRoutes(DesignerSnapshot baseline, IReadOnlyCollection<DesignerNode> movedNodes)
    {
        if (movedNodes.Count == 0) return;
        var movedSet = new HashSet<DesignerNode>(movedNodes);
        var corridors = movedNodes
            .Select(n => new Bounds(n.X, n.Y, n.Width, n.Height).Inflate(CorridorMargin))
            .ToList();

        foreach (var connection in state.Connections)
        {
            bool sourceMoved = movedSet.Contains(connection.Source);
            bool targetMoved = movedSet.Contains(connection.Target);

            if (sourceMoved && targetMoved)
            {
                double dx = baseline.DeltaX(connection.Source);
                double dy = baseline.DeltaY(connection.Source);
                if (ElasticRouter.TryTranslate(connection, dx, dy, state.Nodes, out _))
                    continue;
                ElasticRouter.Route(connection, state.Nodes);
                continue;
            }

            if (sourceMoved || targetMoved)
            {
                ElasticRouter.Route(connection, state.Nodes);
                continue;
            }

            if (corridors.Any(c => c.Intersects(BoundsOf(connection))))
                ElasticRouter.Route(connection, state.Nodes);
        }
    }

    public void RerouteAll()
    {
        foreach (var connection in state.Connections)
            ElasticRouter.Route(connection, state.Nodes);
    }

    public void RouteConnection(DesignerConnection connection) =>
        ElasticRouter.Route(connection, state.Nodes);

    private static Bounds BoundsOf(DesignerConnection connection)
    {
        if (connection.Waypoints.Count == 0) return new Bounds(0, 0, 0, 0);
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var wp in connection.Waypoints)
        {
            minX = Math.Min(minX, wp.X);
            minY = Math.Min(minY, wp.Y);
            maxX = Math.Max(maxX, wp.X);
            maxY = Math.Max(maxY, wp.Y);
        }
        return new Bounds(minX, minY, maxX - minX, maxY - minY);
    }

    private (double X, double Y) OriginOfParent(DesignerSnapshot baseline, BoundaryEvent boundary)
    {
        var parent = state.Nodes.FirstOrDefault(n => n.NodeData.Id == boundary.ParentNodeId);
        return parent == null ? baseline.OriginOf(state.Nodes.First(n => n.NodeData.Id == boundary.ParentNodeId))
            : baseline.OriginOf(parent);
    }

    public IEnumerable<DesignerNode> AttachedBoundaryEvents(IEnumerable<DesignerNode> parents)
    {
        var ids = parents.Select(p => p.NodeData.Id).ToHashSet();
        return state.Nodes
            .Where(n => n.NodeData is BoundaryEvent boundary && ids.Contains(boundary.ParentNodeId))
            .ToList();
    }

    private bool IsDetached(DesignerNode node, HashSet<DesignerNode> moving) =>
        node.NodeData is BoundaryEvent boundary &&
        !state.Nodes.Any(n => n.NodeData.Id == boundary.ParentNodeId && moving.Contains(n));

    private DesignerNode? ParentOf(DesignerNode node) =>
        node.NodeData is BoundaryEvent boundary
            ? state.Nodes.FirstOrDefault(n => n.NodeData.Id == boundary.ParentNodeId)
            : null;

    public DesignerPool? PoolAt(double cx, double cy) =>
        state.Pools.LastOrDefault(p =>
            cx >= p.X && cx <= p.X + p.Width && cy >= p.Y && cy <= p.Y + p.Height);

    public static bool IsNodeInPool(DesignerNode node, DesignerPool pool)
    {
        double cx = node.X + node.Width / 2;
        double cy = node.Y + node.Height / 2;
        return cx >= pool.X && cx <= pool.X + pool.Width &&
               cy >= pool.Y && cy <= pool.Y + pool.Height;
    }

    /// <summary>
    /// Keeps a node inside the content area of a pool and away from lane dividers, so it
    /// cannot land in a header strip or straddle a boundary.
    /// </summary>
    public (double X, double Y) ClampToPoolContent(
        DesignerPool pool, double rawX, double rawY, double nodeW, double nodeH)
    {
        double x = Math.Clamp(rawX,
            pool.X + PoolHeaderWidth + LaneHeaderWidth,
            Math.Max(pool.X + PoolHeaderWidth + LaneHeaderWidth, pool.X + pool.Width - nodeW));
        double y = Math.Clamp(rawY, pool.Y, Math.Max(pool.Y, pool.Y + pool.Height - nodeH));

        double centerY = y + nodeH / 2;
        var lanes = state.Lanes.Where(l => l.Pool == pool).OrderBy(l => l.Data.Order).ToList();
        for (int i = 0; i < lanes.Count - 1; i++)
        {
            double dividerY = lanes[i].Y + lanes[i].Height;
            double distance = centerY - dividerY;
            if (Math.Abs(distance) < LaneBorderZone)
            {
                y = distance <= 0
                    ? dividerY - LaneBorderZone - nodeH / 2
                    : dividerY + LaneBorderZone - nodeH / 2;
                y = Math.Clamp(y, pool.Y, Math.Max(pool.Y, pool.Y + pool.Height - nodeH));
                break;
            }
        }
        return (x, y);
    }

    public readonly record struct Bounds(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;

        public Bounds Inflate(double amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

        public bool Intersects(Bounds other) =>
            X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
    }

    /// <summary>Which edges of a node a resize handle controls.</summary>
    public readonly record struct ResizeEdges
    {
        public bool Top { get; init; }
        public bool Bottom { get; init; }
        public bool Left { get; init; }
        public bool Right { get; init; }

        public static ResizeEdges TopEdge => new() { Top = true };
        public static ResizeEdges BottomEdge => new() { Bottom = true };
        public static ResizeEdges LeftEdge => new() { Left = true };
        public static ResizeEdges RightEdge => new() { Right = true };
        public static ResizeEdges TopLeft => new() { Top = true, Left = true };
        public static ResizeEdges TopRight => new() { Top = true, Right = true };
        public static ResizeEdges BottomLeft => new() { Bottom = true, Left = true };
        public static ResizeEdges BottomRight => new() { Bottom = true, Right = true };
    }
}
