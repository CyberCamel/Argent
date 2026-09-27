using Argent.Core.Workflows;
using Argent.Core.Workflows.Modeler;

namespace Argent.WebComponents.Workflows.Modeler.Routing;

/// <summary>
/// The geometry of a path expressed as axis-aligned lines, the representation the elastic
/// router works with. A path is <c>[S, bend… , T]</c>; each segment between two
/// consecutive points lies on exactly one line, and every line is pinned by a single
/// coordinate (x for vertical, y for horizontal).
/// </summary>
internal sealed class AxisPath
{
    internal readonly List<char> _structure = [];   // 'H' or 'V' per segment
    internal readonly List<double> _coordinates = []; // coordinate of the line for each segment

    public IReadOnlyList<char> Structure => _structure;
    public IReadOnlyList<double> Coordinates => _coordinates;

    public int SegmentCount => _structure.Count;

    public static AxisPath FromWaypoints(IReadOnlyList<DesignerWaypoint> waypoints)
    {
        var path = new AxisPath();
        if (waypoints.Count < 2) return path;

        // One entry per segment, and one coordinate per line between two bends.
        for (int i = 1; i < waypoints.Count; i++)
        {
            bool horizontal = Math.Abs(waypoints[i].Y - waypoints[i - 1].Y) < RoutingService.Epsilon;
            path._structure.Add(horizontal ? 'H' : 'V');
            if (i < waypoints.Count - 1)
                path._coordinates.Add(horizontal ? waypoints[i].Y : waypoints[i].X);
        }
        return path;
    }

    /// <summary>The axis of the line carrying segment <paramref name="segment"/>.</summary>
    public RouteAxis AxisOf(int segment) =>
        _structure[segment] == 'V' ? RouteAxis.Vertical : RouteAxis.Horizontal;

    public double CoordinateOf(int segment) => _coordinates[segment];

    public void SetCoordinate(int segment, double value) => _coordinates[segment] = value;

    /// <summary>Reconstructs waypoints from the endpoints and the pinned line coordinates.</summary>
    public List<DesignerWaypoint> ToWaypoints(DesignerWaypoint start, DesignerWaypoint end)
    {
        var result = new List<DesignerWaypoint> { new() { X = start.X, Y = start.Y } };
        if (_coordinates.Count < SegmentCount) return [start, end];

        // Each bend sits where two consecutive lines meet.
        for (int i = 0; i + 1 < SegmentCount; i++)
        {
            bool vertical = _structure[i] == 'V';
            result.Add(new DesignerWaypoint
            {
                X = vertical ? _coordinates[i] : _coordinates[i + 1],
                Y = vertical ? _coordinates[i + 1] : _coordinates[i]
            });
        }

        result.Add(new DesignerWaypoint { X = end.X, Y = end.Y });
        RoutingService.RemoveCollinearWaypoints(result);
        return result;
    }
}

/// <summary>
/// Rebuilds a connection path from the user's stored route intent, so a manually bent
/// connection stretches when its endpoints move instead of being replaced by an automatic
/// route. Automatic geometry and manual intent stay separate: the automatic router
/// (<see cref="RoutingService"/>) produces the fallback path and the fill coordinates, and
/// this type only overrides the lines the user actually positioned.
/// </summary>
public static class ElasticRouter
{
    /// <summary>
    /// Tolerance for treating a stored line as still in place. A small hysteresis keeps a
    /// route from flickering between its elastic and automatic form near the validity limit.
    /// </summary>
    private const double PositionTolerance = 0.5;

    /// <summary>Routes <paramref name="connection"/> and writes the result to its waypoints.</summary>
    /// <returns>True when the stored intent produced the path, false when the automatic
    /// fallback was used because the intent is currently unsatisfiable.</returns>
    public static bool Route(DesignerConnection connection, IEnumerable<IDesignerItem>? obstacles)
    {
        var route = connection.EngineConnection.Route;
        var (sourceDir, targetDir) = PreferredDirections(connection, route);
        var fallback = RoutingService.AutoRoute(
            connection.Source, sourceDir, connection.Target, targetDir, obstacles: obstacles);

        connection.SourceDir = sourceDir;
        connection.TargetDir = targetDir;

        if (route != null && !string.IsNullOrEmpty(route.Structure) &&
            TryBuild(connection, route, sourceDir, targetDir, fallback, obstacles, out var elastic))
        {
            connection.Waypoints = elastic;
            connection.RouteSuspended = false;
            return true;
        }

        connection.Waypoints = fallback;
        connection.RouteSuspended = route is { Structure.Length: > 0 };
        return false;
    }

    /// <summary>
    /// Moves a whole path and its intent by the same delta. Used when both endpoints of a
    /// connection move together (group and pool drags) so the route does not have to be
    /// recomputed. The intent moves with the path, and the translated result is validated
    /// against the obstacles that actually changed.
    /// </summary>
    public static bool TryTranslate(
        DesignerConnection connection, double dx, double dy,
        IEnumerable<IDesignerItem>? obstacles, out bool stillValid)
    {
        stillValid = false;
        if (connection.Waypoints.Count < 2) return false;
        if (Math.Abs(dx) < RoutingService.Epsilon && Math.Abs(dy) < RoutingService.Epsilon)
        {
            stillValid = true;
            return true;
        }

        foreach (var wp in connection.Waypoints)
        {
            wp.X += dx;
            wp.Y += dy;
        }

        var route = connection.EngineConnection.Route;
        if (route != null)
        {
            foreach (var segment in route.Segments)
            {
                if (segment.Axis == RouteAxis.Vertical) segment.X += dx;
                else segment.Y += dy;
            }
        }

        stillValid = IsValid(connection, connection.SourceDir, connection.TargetDir, obstacles);

        // A translation is only meaningful while the path still starts and ends on the
        // ports it is attached to. If it does not, the delta did not correspond to the
        // actual node movement and the path has to be rebuilt instead.
        if (stillValid) stillValid = EndsOnPorts(connection);
        if (stillValid) return true;

        // The translated result is unusable, so undo the translation. The intent stays where
        // the user put it and a fresh route is computed from the unshifted constraints.
        foreach (var wp in connection.Waypoints)
        {
            wp.X -= dx;
            wp.Y -= dy;
        }
        if (route != null)
        {
            foreach (var segment in route.Segments)
            {
                if (segment.Axis == RouteAxis.Vertical) segment.X -= dx;
                else segment.Y -= dy;
            }
        }
        return false;
    }

    /// <summary>Records the user's current path as the intent for <paramref name="connection"/>.</summary>
    public static void CommitIntent(DesignerConnection connection, IEnumerable<IDesignerItem>? obstacles)
    {
        if (connection.Waypoints.Count < 2) return;

        var route = new ConnectionRoute
        {
            SourceSide = connection.SourceDir,
            TargetSide = connection.TargetDir
        };

        var current = AxisPath.FromWaypoints(connection.Waypoints);
        route.Structure = new string([.. current.Structure]);
        if (current.SegmentCount < 2)
        {
            // A single straight segment has nothing for the user to have positioned.
            connection.EngineConnection.Route = null;
            return;
        }

        // Lines the user moved are the ones that differ from what the automatic router
        // would have chosen. Everything else keeps stretching freely.
        var automatic = AxisPath.FromWaypoints(RoutingService.AutoRoute(
            connection.Source, connection.SourceDir, connection.Target, connection.TargetDir,
            obstacles: obstacles));
        bool comparable = automatic.Structure.Count == current.Structure.Count &&
            new string([.. automatic.Structure]) == route.Structure;

        var counters = new Dictionary<RouteAxis, int>();
        for (int i = 1; i < current.SegmentCount - 1; i++)
        {
            var axis = current.AxisOf(i);
            int ordinal = counters.GetValueOrDefault(axis);
            counters[axis] = ordinal + 1;

            double value = current.CoordinateOf(i);
            if (comparable && Math.Abs(value - automatic.CoordinateOf(i)) <= PositionTolerance)
                continue;   // the automatic route already puts this line here

            route.Segments.Add(new RouteSegment
            {
                Axis = axis,
                X = axis == RouteAxis.Vertical ? value : null,
                Y = axis == RouteAxis.Horizontal ? value : null,
                Index = ordinal
            });
        }

        connection.EngineConnection.Route = route;
        Route(connection, obstacles);
    }

    /// <summary>Clears the manual intent so the connection is routed automatically again.</summary>
    public static void ResetIntent(DesignerConnection connection, IEnumerable<IDesignerItem>? obstacles)
    {
        connection.EngineConnection.Route = null;
        Route(connection, obstacles);
    }

    public static bool HasIntent(DesignerConnection connection) =>
        connection.EngineConnection.Route is { Structure.Length: > 0 };

    /// <summary>
    /// True when the connection's current waypoints satisfy everything the router
    /// promises. Callers use this to stop a manual edit at the last valid position instead
    /// of letting it produce a reversed approach or a crossing.
    /// </summary>
    public static bool IsRouteValid(DesignerConnection connection, IEnumerable<IDesignerItem>? obstacles) =>
        IsValid(connection, connection.SourceDir, connection.TargetDir, obstacles) &&
        EndsOnPorts(connection);

    /// <summary>
    /// True when the path's first and last points are exactly the ports the connection is
    /// attached to. A translated path that no longer starts and ends on its ports has
    /// drifted away from the nodes and must not be rendered.
    /// </summary>
    private static bool EndsOnPorts(DesignerConnection connection)
    {
        if (connection.Waypoints.Count < 2) return false;
        var (sx, sy, _) = AnchorService.GetBaseAnchor(connection.Source, connection.SourceDir);
        var (tx, ty, _) = AnchorService.GetBaseAnchor(connection.Target, connection.TargetDir);
        return Nearly(connection.Waypoints[0].X, sx) && Nearly(connection.Waypoints[0].Y, sy) &&
               Nearly(connection.Waypoints[^1].X, tx) && Nearly(connection.Waypoints[^1].Y, ty);
    }

    private static bool Nearly(double a, double b) => Math.Abs(a - b) < RoutingService.Epsilon;

    private static (AnchorDirection Source, AnchorDirection Target) PreferredDirections(
        DesignerConnection connection, ConnectionRoute? route)
    {
        var best = RoutingService.GetBestDirections(connection.Source, connection.Target);
        if (route == null) return best;

        var source = route.SourceSide == AnchorDirection.None ? best.sourceDir : route.SourceSide;
        var target = route.TargetSide == AnchorDirection.None ? best.targetDir : route.TargetSide;
        return (source, target);
    }

    private static bool TryBuild(
        DesignerConnection connection, ConnectionRoute route,
        AnchorDirection sourceDir, AnchorDirection targetDir,
        List<DesignerWaypoint> fallback, IEnumerable<IDesignerItem>? obstacles,
        out List<DesignerWaypoint> waypoints)
    {
        waypoints = [];

        var (sx, sy, _) = AnchorService.GetBaseAnchor(connection.Source, sourceDir);
        var (tx, ty, _) = AnchorService.GetBaseAnchor(connection.Target, targetDir);
        var start = new DesignerWaypoint { X = sx, Y = sy };
        var end = new DesignerWaypoint { X = tx, Y = ty };

        var structure = route.Structure;
        if (structure.Length < 2) return false;
        if (!IsHorizontalDir(sourceDir) != (structure[0] == 'V')) return false;
        if (!IsHorizontalDir(targetDir) != (structure[^1] == 'V')) return false;

        // Fill coordinates come from the automatic route when its structure agrees, so an
        // unchanged route reproduces exactly; otherwise they are interpolated between the
        // lines that do have constraints.
        var automatic = AxisPath.FromWaypoints(fallback);
        bool aligned = automatic.SegmentCount == structure.Length &&
            new string([.. automatic.Structure]) == structure;

        var path = new AxisPath();
        var coordinates = new double[structure.Length];
        for (int i = 0; i < structure.Length; i++)
        {
            path._structure.Add(structure[i]);
            // Only interior lines have a stored coordinate; the first and last belong to
            // the ports they run to and are fixed below.
            coordinates[i] = aligned && i > 0 && i < structure.Length - 1
                ? automatic.CoordinateOf(i)
                : double.NaN;
        }

        // The first and last lines are fixed by the ports they run to.
        if (structure[0] == 'V') coordinates[0] = start.X; else coordinates[0] = start.Y;
        if (structure[^1] == 'V') coordinates[^1] = end.X; else coordinates[^1] = end.Y;
        for (int i = 0; i < structure.Length; i++) path._coordinates.Add(coordinates[i]);

        foreach (var segment in route.Segments)
        {
            int index = FindSegmentSlot(path, segment);
            if (index >= 0) coordinates[index] = segment.Value;
        }

        FillGaps(path, coordinates, start, end);
        for (int i = 0; i < structure.Length; i++) path.SetCoordinate(i, coordinates[i]);

        var candidate = path.ToWaypoints(start, end);
        if (candidate.Count < 2) return false;
        if (!IsValidPath(candidate, connection.Source, sourceDir, connection.Target, targetDir, obstacles))
            return false;

        waypoints = candidate;
        return true;
    }

    /// <summary>
    /// Maps a stored constraint onto a line of the rebuilt path. Ordinals are only trusted
    /// when the structure matched, and the guard below rejects a constraint that would sit
    /// on the port stub itself, so a stale entry can never bend the first or last segment.
    /// </summary>
    private static int FindSegmentSlot(AxisPath path, RouteSegment segment)
    {
        int count = 0;
        for (int i = 1; i < path.SegmentCount - 1; i++)
        {
            if (path.AxisOf(i) != segment.Axis) continue;
            if (count == Math.Max(0, segment.Index)) return i;
            count++;
        }
        return -1;
    }

    private static void FillGaps(AxisPath path, double[] coordinates, DesignerWaypoint start, DesignerWaypoint end)
    {
        int count = coordinates.Length;
        int i = 0;
        while (i < count)
        {
            if (!double.IsNaN(coordinates[i])) { i++; continue; }

            int startOfGap = i;
            while (i < count && double.IsNaN(coordinates[i])) i++;
            int endOfGap = i - 1;

            bool hasLeft = startOfGap > 0;
            bool hasRight = endOfGap < count - 1;
            int slots = endOfGap - startOfGap + 1;

            for (int k = 0; k < slots; k++)
            {
                int index = startOfGap + k;
                double fraction = (double)(k + 1) / (slots + 1);

                if (hasLeft && hasRight)
                {
                    coordinates[index] = coordinates[startOfGap - 1] +
                        (coordinates[endOfGap + 1] - coordinates[startOfGap - 1]) * fraction;
                }
                else if (hasRight)
                {
                    // Detour outward from the constrained side so the turn stays outside the
                    // straight line between the ports.
                    double anchor = coordinates[endOfGap + 1];
                    double toward = AxisStartCoordinate(path.AxisOf(index), start, end);
                    coordinates[index] = toward < anchor ? anchor - RoutingService.DefaultOffset : anchor + RoutingService.DefaultOffset;
                }
                else if (hasLeft)
                {
                    double anchor = coordinates[startOfGap - 1];
                    double toward = AxisStartCoordinate(path.AxisOf(index), start, end);
                    coordinates[index] = toward < anchor ? anchor - RoutingService.DefaultOffset : anchor + RoutingService.DefaultOffset;
                }
                else
                {
                    coordinates[index] = AxisStartCoordinate(path.AxisOf(index), start, end);
                }
            }
        }
    }

    private static double AxisStartCoordinate(RouteAxis axis, DesignerWaypoint start, DesignerWaypoint end) =>
        axis == RouteAxis.Vertical
            ? (start.X + end.X) / 2
            : (start.Y + end.Y) / 2;

    private static bool IsHorizontalDir(AnchorDirection dir) =>
        dir is AnchorDirection.Left or AnchorDirection.Right;

    internal static bool IsValid(
        DesignerConnection connection, AnchorDirection sourceDir, AnchorDirection targetDir,
        IEnumerable<IDesignerItem>? obstacles) =>
        IsValidPath(connection.Waypoints, connection.Source, sourceDir, connection.Target, targetDir, obstacles);

    /// <summary>
    /// Checks the properties the router promises: orthogonal segments, a clear run out of
    /// the source port and into the target port in the port's own direction, enough room
    /// for the arrowhead, and no crossing of any node.
    /// </summary>
    internal static bool IsValidPath(
        List<DesignerWaypoint> waypoints, IDesignerItem source, AnchorDirection sourceDir,
        IDesignerItem target, AnchorDirection targetDir, IEnumerable<IDesignerItem>? obstacles)
    {
        if (waypoints.Count < 2) return false;

        var first = waypoints[0];
        var second = waypoints[1];
        var last = waypoints[^1];
        var beforeLast = waypoints[^2];

        if (!Leaves(first, second, sourceDir)) return false;
        if (!Leaves(last, beforeLast, targetDir)) return false;
        if (Distance(first, second) < RoutingService.MinArrowSpace) return false;
        if (Distance(last, beforeLast) < RoutingService.MinArrowSpace) return false;

        var endpointBoxes = new List<RoutingService.RouteBox> { RoutingService.RouteBox.Around(source, 1) };
        if (!ReferenceEquals(source, target)) endpointBoxes.Add(RoutingService.RouteBox.Around(target, 1));

        var boxes = new List<RoutingService.RouteBox>(endpointBoxes);
        if (obstacles != null)
            boxes.AddRange(obstacles
                .Where(n => !ReferenceEquals(n, source) && !ReferenceEquals(n, target))
                .Select(n => RoutingService.RouteBox.Around(n, 16)));

        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            var a = waypoints[i];
            var b = waypoints[i + 1];
            if (Math.Abs(a.X - b.X) > RoutingService.Epsilon && Math.Abs(a.Y - b.Y) > RoutingService.Epsilon)
                return false;   // not orthogonal
            for (int k = 0; k < boxes.Count; k++)
            {
                // The run out of the source port and the run into the target port begin on
                // the node itself, so their own node boxes do not apply. Every other
                // obstacle still does.
                if (k < endpointBoxes.Count && (i == 0 || i == waypoints.Count - 2)) continue;
                if (boxes[k].Cuts((a.X, a.Y), (b.X, b.Y))) return false;
            }
        }
        return true;
    }

    /// <summary>True when the segment runs from <paramref name="from"/> towards <paramref name="direction"/>.</summary>
    private static bool Leaves(DesignerWaypoint from, DesignerWaypoint to, AnchorDirection direction)
    {
        double dx = Math.Sign(to.X - from.X);
        double dy = Math.Sign(to.Y - from.Y);
        return direction switch
        {
            AnchorDirection.Left => dx < 0 && dy == 0,
            AnchorDirection.Right => dx > 0 && dy == 0,
            AnchorDirection.Top => dy < 0 && dx == 0,
            AnchorDirection.Bottom => dy > 0 && dx == 0,
            _ => false
        };
    }

    private static double Distance(DesignerWaypoint a, DesignerWaypoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
