using Argent.Core.Workflows;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace Argent.WebComponents.Workflows.Modeler.Routing;

public static class RoutingService
{
    private static double GetDirDx(AnchorDirection dir) => dir switch
    {
        AnchorDirection.Right => 1,
        AnchorDirection.Left => -1,
        _ => 0
    };

    private static double GetDirDy(AnchorDirection dir) => dir switch
    {
        AnchorDirection.Bottom => 1,
        AnchorDirection.Top => -1,
        _ => 0
    };

    private static bool IsHorizontal(AnchorDirection dir) =>
        dir is AnchorDirection.Left or AnchorDirection.Right;

    internal const double Epsilon = 0.001;
    internal const double DefaultOffset = 40;
    private const double CornerRadius = 8;
    internal const double MinArrowSpace = 18;

    private static bool SharesX(double a, double b) => Math.Abs(a - b) < Epsilon;
    private static bool SharesY(double a, double b) => Math.Abs(a - b) < Epsilon;

    public static List<DesignerWaypoint> AutoRoute(
        IDesignerItem source, AnchorDirection sourceDir,
        IDesignerItem target, AnchorDirection targetDir,
        double offset = DefaultOffset,
        IEnumerable<IDesignerItem>? obstacles = null)
    {
        var (sx, sy, _) = AnchorService.GetBaseAnchor(source, sourceDir);
        var (tx, ty, _) = AnchorService.GetBaseAnchor(target, targetDir);
        var clearance = Math.Max(MinArrowSpace + 4, Math.Min(offset, DefaultOffset));
        var start = (X: sx + clearance * GetDirDx(sourceDir), Y: sy + clearance * GetDirDy(sourceDir));
        var end = (X: tx + clearance * GetDirDx(targetDir), Y: ty + clearance * GetDirDy(targetDir));

        // Route between outward-facing port stubs. The node bounds are obstacles too,
        // so reversed and close connections go around their shapes instead of through them.
        var boxes = new List<RouteBox> { RouteBox.Around(source, 1) };
        if (!ReferenceEquals(source, target)) boxes.Add(RouteBox.Around(target, 1));
        if (obstacles != null)
            boxes.AddRange(obstacles.Where(n => !ReferenceEquals(n, source) && !ReferenceEquals(n, target))
                .Select(n => RouteBox.Around(n, 16)));

        var middle = FindClearPath(start, end, boxes);
        var wps = new List<DesignerWaypoint> { new() { X = sx, Y = sy } };
        wps.AddRange(middle.Select(p => new DesignerWaypoint { X = p.X, Y = p.Y }));
        wps.Add(new DesignerWaypoint { X = tx, Y = ty });
        RemoveCollinearWaypoints(wps);
        return wps;
    }

    internal readonly record struct RouteBox(double Left, double Top, double Right, double Bottom)
    {
        public static RouteBox Around(IDesignerItem node, double margin) =>
            new(node.X - margin, node.Y - margin, node.X + node.Width + margin, node.Y + node.Height + margin);

        public bool Contains(double x, double y) =>
            x > Left + Epsilon && x < Right - Epsilon && y > Top + Epsilon && y < Bottom - Epsilon;

        public bool Cuts((double X, double Y) a, (double X, double Y) b) =>
            SharesY(a.Y, b.Y)
                ? a.Y > Top + Epsilon && a.Y < Bottom - Epsilon && Math.Max(a.X, b.X) > Left + Epsilon && Math.Min(a.X, b.X) < Right - Epsilon
                : a.X > Left + Epsilon && a.X < Right - Epsilon && Math.Max(a.Y, b.Y) > Top + Epsilon && Math.Min(a.Y, b.Y) < Bottom - Epsilon;
    }

    private static bool IsClear((double X, double Y) a, (double X, double Y) b, List<RouteBox> boxes)
    {
        foreach (var box in boxes)
            if (box.Cuts(a, b)) return false;
        return true;
    }

    private static List<(double X, double Y)> FindClearPath(
        (double X, double Y) start, (double X, double Y) end, List<RouteBox> boxes)
    {
        // Most connections need no graph. These candidates have the shortest
        // Manhattan length, and still check every obstacle before accepting them.
        if ((SharesX(start.X, end.X) || SharesY(start.Y, end.Y)) && IsClear(start, end, boxes))
            return [start, end];
        var bend = (X: end.X, Y: start.Y);
        if (IsClear(start, bend, boxes) && IsClear(bend, end, boxes))
            return [start, bend, end];
        bend = (start.X, end.Y);
        if (IsClear(start, bend, boxes) && IsClear(bend, end, boxes))
            return [start, bend, end];

        var xs = new SortedSet<double> { start.X, end.X };
        var ys = new SortedSet<double> { start.Y, end.Y };
        foreach (var box in boxes)
        {
            xs.Add(box.Left); xs.Add(box.Right);
            ys.Add(box.Top); ys.Add(box.Bottom);
        }
        var xValues = xs.ToArray();
        var yValues = ys.ToArray();
        int width = xValues.Length, height = yValues.Length;
        int vertices = width * height;

        // Mark blocked edges once using interval differences. The old search
        // scanned every box at every grid point and again at every visited edge.
        // This takes O(n * (width + height) + width * height), rather than O(n^3).
        var horizontal = ArrayPool<int>.Shared.Rent(vertices);
        var vertical = ArrayPool<int>.Shared.Rent(vertices);
        var costs = ArrayPool<double>.Shared.Rent(vertices * 2);
        var previous = ArrayPool<int>.Shared.Rent(vertices * 2);
        try
        {
            Array.Clear(horizontal, 0, vertices);
            Array.Clear(vertical, 0, vertices);
            foreach (var box in boxes)
            {
                int left = Array.BinarySearch(xValues, box.Left);
                int right = Array.BinarySearch(xValues, box.Right);
                int top = Array.BinarySearch(yValues, box.Top);
                int bottom = Array.BinarySearch(yValues, box.Bottom);
                for (int y = top + 1; y < bottom; y++)
                {
                    if (yValues[y] <= box.Top + Epsilon || yValues[y] >= box.Bottom - Epsilon) continue;
                    horizontal[y * width + left]++;
                    horizontal[y * width + right]--;
                }
                for (int x = left + 1; x < right; x++)
                {
                    if (xValues[x] <= box.Left + Epsilon || xValues[x] >= box.Right - Epsilon) continue;
                    vertical[top * width + x]++;
                    vertical[bottom * width + x]--;
                }
            }
            for (int y = 0; y < height; y++)
                for (int x = 1; x < width; x++)
                    horizontal[y * width + x] += horizontal[y * width + x - 1];
            for (int y = 1; y < height; y++)
                for (int x = 0; x < width; x++)
                    vertical[y * width + x] += vertical[(y - 1) * width + x];

            int startIndex = Array.BinarySearch(yValues, start.Y) * width + Array.BinarySearch(xValues, start.X);
            int endIndex = Array.BinarySearch(yValues, end.Y) * width + Array.BinarySearch(xValues, end.X);
            Array.Fill(costs, double.PositiveInfinity, 0, vertices * 2);
            Array.Fill(previous, -1, 0, vertices * 2);

            // A* uses Manhattan distance as its lower bound. Each vertex keeps two
            // incoming-axis states so the bend penalty still participates in routing.
            var queue = new PriorityQueue<(int State, double Cost), double>();
            costs[startIndex * 2] = costs[startIndex * 2 + 1] = 0;
            double initialDistance = Math.Abs(start.X - end.X) + Math.Abs(start.Y - end.Y);
            queue.Enqueue((startIndex * 2, 0), initialDistance);
            queue.Enqueue((startIndex * 2 + 1, 0), initialDistance);
            int finish = -1;
            while (queue.TryDequeue(out var current, out _))
            {
                int state = current.State;
                if (current.Cost > costs[state] + Epsilon) continue;
                int vertex = state / 2, incomingAxis = state % 2;
                if (vertex == endIndex) { finish = state; break; }
                int x = vertex % width, y = vertex / width;
                if (x > 0 && horizontal[vertex - 1] == 0)
                    Visit(vertex - 1, 0, xValues[x] - xValues[x - 1]);
                if (x + 1 < width && horizontal[vertex] == 0)
                    Visit(vertex + 1, 0, xValues[x + 1] - xValues[x]);
                if (y > 0 && vertical[vertex - width] == 0)
                    Visit(vertex - width, 1, yValues[y] - yValues[y - 1]);
                if (y + 1 < height && vertical[vertex] == 0)
                    Visit(vertex + width, 1, yValues[y + 1] - yValues[y]);

                void Visit(int next, int axis, double distance)
                {
                    double cost = current.Cost + distance + (incomingAxis != axis ? 16 : 0);
                    int nextState = next * 2 + axis;
                    if (cost + Epsilon >= costs[nextState]) return;
                    costs[nextState] = cost;
                    previous[nextState] = state;
                    double remaining = Math.Abs(xValues[next % width] - end.X) + Math.Abs(yValues[next / width] - end.Y);
                    queue.Enqueue((nextState, cost), cost + remaining);
                }
            }

            // Overlapping nodes can enclose a port: retain the existing fallback.
            if (finish < 0) return [start, (start.X, end.Y), end];

            var path = new List<(double X, double Y)>();
            for (int state = finish; state >= 0; state = previous[state])
            {
                int vertex = state / 2;
                path.Add((xValues[vertex % width], yValues[vertex / width]));
            }
            path.Reverse();
            return path;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(horizontal);
            ArrayPool<int>.Shared.Return(vertical);
            ArrayPool<double>.Shared.Return(costs);
            ArrayPool<int>.Shared.Return(previous);
        }
    }

    public static string DraftPath(
        IDesignerItem source, AnchorDirection sourceDir,
        double mouseX, double mouseY,
        IDesignerItem? targetHint = null,
        double offset = DefaultOffset)
    {
        if (targetHint != null)
        {
            var (_, _, targetDir) = AnchorService.GetClosestAnchor(targetHint, (mouseX, mouseY));
            var wps = AutoRoute(source, sourceDir, targetHint, targetDir, offset);
            return WaypointsToSvgPath(wps);
        }

        var (sx, sy, _) = AnchorService.GetBaseAnchor(source, sourceDir);

        if (IsHorizontal(sourceDir))
        {
            double ex = sx + offset * GetDirDx(sourceDir);
            return string.Create(CultureInfo.InvariantCulture,
                $"M {sx} {sy} L {ex} {sy} L {ex} {mouseY} L {mouseX} {mouseY}");
        }
        else
        {
            double ey = sy + offset * GetDirDy(sourceDir);
            return string.Create(CultureInfo.InvariantCulture,
                $"M {sx} {sy} L {sx} {ey} L {mouseX} {ey} L {mouseX} {mouseY}");
        }
    }

    public static string DraftPathToTarget(
        IDesignerItem target, AnchorDirection targetDir,
        double mouseX, double mouseY,
        IDesignerItem? sourceHint = null,
        double offset = DefaultOffset)
    {
        if (sourceHint != null)
        {
            var (_, _, sourceDir) = AnchorService.GetClosestAnchor(sourceHint, (mouseX, mouseY));
            var wps = AutoRoute(sourceHint, sourceDir, target, targetDir, offset);
            return WaypointsToSvgPath(wps);
        }

        var (tx, ty, _) = AnchorService.GetBaseAnchor(target, targetDir);

        if (IsHorizontal(targetDir))
        {
            double ex = tx + offset * GetDirDx(targetDir);
            return string.Create(CultureInfo.InvariantCulture,
                $"M {mouseX} {mouseY} L {ex} {mouseY} L {ex} {ty} L {tx} {ty}");
        }
        else
        {
            double ey = ty + offset * GetDirDy(targetDir);
            return string.Create(CultureInfo.InvariantCulture,
                $"M {mouseX} {mouseY} L {mouseX} {ey} L {tx} {ey} L {tx} {ty}");
        }
    }

    public static string WaypointsToSvgPath(List<DesignerWaypoint> waypoints, double cornerRadius = CornerRadius)
    {
        if (waypoints.Count < 2) return "";

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"M {waypoints[0].X} {waypoints[0].Y}");

        for (int i = 1; i < waypoints.Count; i++)
        {
            bool appendedCorner = false;

            if (cornerRadius > 0 && i < waypoints.Count - 1)
            {
                appendedCorner = TryAppendCornerArc(
                    sb, waypoints[i - 1], waypoints[i], waypoints[i + 1], cornerRadius);
            }

            if (!appendedCorner)
            {
                sb.Append(CultureInfo.InvariantCulture, $" L {waypoints[i].X} {waypoints[i].Y}");
            }
        }

        return sb.ToString();
    }

    private static bool TryAppendCornerArc(
        StringBuilder sb,
        DesignerWaypoint a, DesignerWaypoint b, DesignerWaypoint c,
        double r)
    {
        bool horizThenVert = SharesY(a.Y, b.Y) && SharesX(b.X, c.X);
        bool vertThenHoriz = SharesX(a.X, b.X) && SharesY(b.Y, c.Y);

        if (!horizThenVert && !vertThenHoriz)
            return false;

        double len1 = horizThenVert
            ? Math.Abs(b.X - a.X)
            : Math.Abs(b.Y - a.Y);

        double len2 = horizThenVert
            ? Math.Abs(c.Y - b.Y)
            : Math.Abs(c.X - b.X);

        r = Math.Min(r, Math.Min(len1, len2) * 0.5);
        if (r <= 1) return false;

        double arcStartX, arcStartY, arcEndX, arcEndY;

        if (horizThenVert)
        {
            double signX = Math.Sign(b.X - a.X);
            double signY = Math.Sign(c.Y - b.Y);
            arcStartX = b.X - signX * r;
            arcStartY = b.Y;
            arcEndX = b.X;
            arcEndY = b.Y + signY * r;
        }
        else
        {
            double signY = Math.Sign(b.Y - a.Y);
            double signX = Math.Sign(c.X - b.X);
            arcStartX = b.X;
            arcStartY = b.Y - signY * r;
            arcEndX = b.X + signX * r;
            arcEndY = b.Y;
        }

        sb.Append(CultureInfo.InvariantCulture, $" L {arcStartX} {arcStartY}");
        sb.Append(CultureInfo.InvariantCulture, $" Q {b.X} {b.Y} {arcEndX} {arcEndY}");
        return true;
    }

    public static void RemoveCollinearWaypoints(List<DesignerWaypoint> wps)
    {
        if (wps.Count < 3) return;

        for (int i = wps.Count - 2; i >= 1; i--)
        {
            var prev = wps[i - 1];
            var curr = wps[i];
            var next = wps[i + 1];

            if ((SharesX(prev.X, curr.X) && SharesX(curr.X, next.X)) ||
                (SharesY(prev.Y, curr.Y) && SharesY(curr.Y, next.Y)))
            {
                wps.RemoveAt(i);
            }
        }
    }

    public static (AnchorDirection sourceDir, AnchorDirection targetDir) GetBestDirections(
        IDesignerItem source, IDesignerItem target)
    {
        double scx = source.X + source.Width / 2;
        double scy = source.Y + source.Height / 2;
        double tcx = target.X + target.Width / 2;
        double tcy = target.Y + target.Height / 2;
        double dx = tcx - scx;
        double dy = tcy - scy;

        AnchorDirection sourceDir, targetDir;
        if (Math.Abs(dx) > Math.Abs(dy))
        {
            sourceDir = dx > 0 ? AnchorDirection.Right : AnchorDirection.Left;
            targetDir = dx > 0 ? AnchorDirection.Left : AnchorDirection.Right;
        }
        else
        {
            sourceDir = dy > 0 ? AnchorDirection.Bottom : AnchorDirection.Top;
            targetDir = dy > 0 ? AnchorDirection.Top : AnchorDirection.Bottom;
        }

        // Two nodes that overlap on the dominant axis can end up with a port on the far
        // side of its own node, which makes the connector approach from behind. The side is
        // flipped to the one that actually faces the other node.
        if (!Faces(source, target, sourceDir)) sourceDir = Opposite(sourceDir);
        if (!Faces(target, source, targetDir)) targetDir = Opposite(targetDir);
        return (sourceDir, targetDir);
    }

    private static bool Faces(IDesignerItem self, IDesignerItem other, AnchorDirection dir)
    {
        double cx = self.X + self.Width / 2;
        double cy = self.Y + self.Height / 2;
        double ox = other.X + other.Width / 2;
        double oy = other.Y + other.Height / 2;
        return dir switch
        {
            AnchorDirection.Left => ox < cx,
            AnchorDirection.Right => ox > cx,
            AnchorDirection.Top => oy < cy,
            AnchorDirection.Bottom => oy > cy,
            _ => true
        };
    }

    public static AnchorDirection Opposite(AnchorDirection dir) => dir switch
    {
        AnchorDirection.Left => AnchorDirection.Right,
        AnchorDirection.Right => AnchorDirection.Left,
        AnchorDirection.Top => AnchorDirection.Bottom,
        AnchorDirection.Bottom => AnchorDirection.Top,
        _ => AnchorDirection.None
    };

    public static int FindNearestSegment(List<DesignerWaypoint> wps, double mx, double my, double threshold = 20.0)
    {
        double bestDist = threshold;
        int bestIdx = -1;
        for (int i = 0; i < wps.Count - 1; i++)
        {
            double dist;
            if (SharesY(wps[i].Y, wps[i + 1].Y))
            {
                double minX = Math.Min(wps[i].X, wps[i + 1].X);
                double maxX = Math.Max(wps[i].X, wps[i + 1].X);
                dist = Math.Abs(my - wps[i].Y);
                if (dist < bestDist)
                {
                    double xTol = threshold * 0.5;
                    if (mx >= minX - xTol && mx <= maxX + xTol)
                    {
                        bestDist = dist;
                        bestIdx = i;
                    }
                }
            }
            else
            {
                double minY = Math.Min(wps[i].Y, wps[i + 1].Y);
                double maxY = Math.Max(wps[i].Y, wps[i + 1].Y);
                dist = Math.Abs(mx - wps[i].X);
                if (dist < bestDist)
                {
                    double yTol = threshold * 0.5;
                    if (my >= minY - yTol && my <= maxY + yTol)
                    {
                        bestDist = dist;
                        bestIdx = i;
                    }
                }
            }
        }
        return bestIdx;
    }

    public static bool DragSegment(List<DesignerWaypoint> waypoints, int segmentIndex,
        ref double dragStartX, ref double dragStartY,
        double worldX, double worldY)
    {
        int n = waypoints.Count;
        if (segmentIndex < 0 || segmentIndex >= n - 1) return false;
        if (segmentIndex <= 0 || segmentIndex >= n - 2) return false;

        double dx = worldX - dragStartX;
        double dy = worldY - dragStartY;

        if (SharesY(waypoints[segmentIndex].Y, waypoints[segmentIndex + 1].Y))
        {
            waypoints[segmentIndex].Y += dy;
            waypoints[segmentIndex + 1].Y += dy;
        }
        else
        {
            waypoints[segmentIndex].X += dx;
            waypoints[segmentIndex + 1].X += dx;
        }

        dragStartX = worldX;
        dragStartY = worldY;
        return true;
    }

    public static bool DragWaypoint(List<DesignerWaypoint> waypoints, int index,
        double worldX, double worldY)
    {
        int n = waypoints.Count;
        if (index <= 0 || index >= n - 1) return false;

        var oldCoords = new (double X, double Y)[n];
        for (int k = 0; k < n; k++) oldCoords[k] = (waypoints[k].X, waypoints[k].Y);

        waypoints[index].X = worldX;
        waypoints[index].Y = worldY;

        if (index > 0)
        {
            if (SharesX(oldCoords[index].X, oldCoords[index - 1].X))
            {
                if (index - 1 == 0) waypoints[index].X = waypoints[0].X;
                else waypoints[index - 1].X = waypoints[index].X;
            }
            else if (SharesY(oldCoords[index].Y, oldCoords[index - 1].Y))
            {
                if (index - 1 == 0) waypoints[index].Y = waypoints[0].Y;
                else waypoints[index - 1].Y = waypoints[index].Y;
            }
        }

        if (index < n - 1)
        {
            if (SharesX(oldCoords[index].X, oldCoords[index + 1].X))
            {
                if (index + 1 == n - 1) waypoints[index].X = waypoints[n - 1].X;
                else waypoints[index + 1].X = waypoints[index].X;
            }
            else if (SharesY(oldCoords[index].Y, oldCoords[index + 1].Y))
            {
                if (index + 1 == n - 1) waypoints[index].Y = waypoints[n - 1].Y;
                else waypoints[index + 1].Y = waypoints[index].Y;
            }
        }

        return true;
    }

    public static int InsertWaypointAtMidpoint(List<DesignerWaypoint> waypoints, int segmentIndex)
    {
        if (segmentIndex < 0 || segmentIndex >= waypoints.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));

        var a = waypoints[segmentIndex];
        var b = waypoints[segmentIndex + 1];
        var newWp = new DesignerWaypoint { X = (a.X + b.X) / 2, Y = (a.Y + b.Y) / 2 };
        waypoints.Insert(segmentIndex + 1, newWp);
        return segmentIndex + 1;
    }
}
