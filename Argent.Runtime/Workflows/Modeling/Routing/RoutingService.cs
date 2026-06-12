using Argent.Contracts.Workflows;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Argent.Runtime.Workflows.Modeling.Routing;

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

    private const double Epsilon = 0.001;
    private const double DefaultOffset = 40;
    private const double CornerRadius = 8;
    private const double MinArrowSpace = 18;

    private static bool SharesX(double a, double b) => Math.Abs(a - b) < Epsilon;
    private static bool SharesY(double a, double b) => Math.Abs(a - b) < Epsilon;

    public static List<DesignerWaypoint> AutoRoute(
        IDesignerItem source, AnchorDirection sourceDir,
        IDesignerItem target, AnchorDirection targetDir,
        double offset = DefaultOffset)
    {
        var (sx, sy, _) = AnchorService.GetBaseAnchor(source, sourceDir);
        var (tx, ty, _) = AnchorService.GetBaseAnchor(target, targetDir);

        offset = ClampOffset(source, sourceDir, target, offset);

        bool sH = IsHorizontal(sourceDir);
        bool tH = IsHorizontal(targetDir);

        List<DesignerWaypoint> wps;

        if (sH && tH)
        {
            double mx = sx + offset * GetDirDx(sourceDir);
            wps =
            [
                new() { X = sx, Y = sy },
                new() { X = mx, Y = sy },
                new() { X = mx, Y = ty },
                new() { X = tx, Y = ty },
            ];
        }
        else if (!sH && !tH)
        {
            double my = sy + offset * GetDirDy(sourceDir);
            wps =
            [
                new() { X = sx, Y = sy },
                new() { X = sx, Y = my },
                new() { X = tx, Y = my },
                new() { X = tx, Y = ty },
            ];
        }
        else if (sH)
        {
            wps =
            [
                new() { X = sx, Y = sy },
                new() { X = tx, Y = sy },
                new() { X = tx, Y = ty },
            ];
        }
        else
        {
            wps =
            [
                new() { X = sx, Y = sy },
                new() { X = sx, Y = ty },
                new() { X = tx, Y = ty },
            ];
        }

        RemoveCollinearWaypoints(wps);
        EnsureMinimumLastSegment(wps, MinArrowSpace);
        return wps;
    }

    private static double ClampOffset(
        IDesignerItem source, AnchorDirection sourceDir,
        IDesignerItem target, double offset)
    {
        double scx = source.X + source.Width / 2;
        double scy = source.Y + source.Height / 2;
        double tcx = target.X + target.Width / 2;
        double tcy = target.Y + target.Height / 2;

        double clamped;
        if (IsHorizontal(sourceDir))
        {
            double hDist = Math.Abs(tcx - scx);
            clamped = Math.Min(offset, Math.Max(offset * 0.3, hDist * 0.3));
        }
        else
        {
            double vDist = Math.Abs(tcy - scy);
            clamped = Math.Min(offset, Math.Max(offset * 0.3, vDist * 0.3));
        }

        return Math.Max(clamped, MinArrowSpace);
    }

    private static void EnsureMinimumLastSegment(List<DesignerWaypoint> wps, double minLen)
    {
        int n = wps.Count;
        if (n < 2) return;

        int lastA = n - 2;
        int lastB = n - 1;
        var a = wps[lastA];
        var b = wps[lastB];

        double len;
        bool isHorizontal;

        if (SharesY(a.Y, b.Y))
        {
            len = Math.Abs(b.X - a.X);
            isHorizontal = true;
        }
        else if (SharesX(a.X, b.X))
        {
            len = Math.Abs(b.Y - a.Y);
            isHorizontal = false;
        }
        else
        {
            return;
        }

        if (len >= minLen) return;

        double extension = minLen - len;
        double dx = 0, dy = 0;
        if (isHorizontal)
            dx = Math.Sign(b.X - a.X) * extension;
        else
            dy = Math.Sign(b.Y - a.Y) * extension;

        for (int i = 1; i < n - 1; i++)
        {
            wps[i].X += dx;
            wps[i].Y += dy;
        }

        RemoveCollinearWaypoints(wps);
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

    /// <summary>
    /// Draft path for re-dragging a connection's source endpoint: the path runs from the
    /// mouse to the fixed target so the arrowhead (marker-end) stays on the fixed end.
    /// </summary>
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

        if (Math.Abs(dx) > Math.Abs(dy))
        {
            return dx > 0
                ? (AnchorDirection.Right, AnchorDirection.Left)
                : (AnchorDirection.Left, AnchorDirection.Right);
        }
        else
        {
            return dy > 0
                ? (AnchorDirection.Bottom, AnchorDirection.Top)
                : (AnchorDirection.Top, AnchorDirection.Bottom);
        }
    }

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
