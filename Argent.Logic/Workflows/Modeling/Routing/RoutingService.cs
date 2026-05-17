using Argent.Contracts.Workflows;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Argent.Runtime.Workflows.Modeling.Routing;

public class RoutingService
{

    private const double Offset = 30;

    private enum RouteType
    {
        LShape,
        ZShape
    }

    private enum SegmentDirection
    {
        Horizontal,
        Vertical,
        None
    }

    private static SegmentDirection GetInitialDirection(AnchorDirection dir)
    {
        return dir switch
        {
            AnchorDirection.Left => SegmentDirection.Horizontal,
            AnchorDirection.Right => SegmentDirection.Horizontal,
            AnchorDirection.Top => SegmentDirection.Vertical,
            AnchorDirection.Bottom => SegmentDirection.Vertical,
            _ => SegmentDirection.None
        };
    }

    private static bool IsDirectionCompatible(SegmentDirection current, AnchorDirection next)
    {
        return (current, next) switch
        {
            (SegmentDirection.Horizontal, AnchorDirection.Left) => true,
            (SegmentDirection.Horizontal, AnchorDirection.Right) => true,

            (SegmentDirection.Vertical, AnchorDirection.Top) => true,
            (SegmentDirection.Vertical, AnchorDirection.Bottom) => true,

            _ => false
        };
    }

    // Use this for standard auto-routing
    public static string GetOrthogonalPath(IDesignerItem source, IDesignerItem target)
    {
        var (X, Y, dir) = AnchorService.GetClosestAnchor(source, target);
        var (endX, endY, endDir) = AnchorService.GetClosestAnchor(target, source);

        var sourceAnchors = AnchorService.GetAnchors(source);
        var targetAnchors = AnchorService.GetAnchors(target);

        var candidates = new List<(string path, double score)>();

        foreach (var a in sourceAnchors)
        {
            var initialDir = GetInitialDirection(a.dir);

            foreach (var t in targetAnchors)
            {
                if (!IsDirectionCompatible(initialDir, t.dir))
                    continue;

                // NOW you generate BOTH L and Z for this pair
                candidates.Add(BuildL((a.X, a.Y, a.dir), (t.X, t.Y, t.dir)));
                candidates.Add(BuildZ((a.X, a.Y, a.dir), (t.X, t.Y, t.dir)));
            }
        }

        return candidates
            .OrderBy(c => c.score)
            .First()
            .path;
    }

    // NEW: Use this when you want to force specific directions (Fixes your re-route!)
    public static string GetOrthogonalPath(IDesignerItem source, AnchorDirection sourceDir, IDesignerItem target, AnchorDirection targetDir)
    {
        var (X, Y, dir) = AnchorService.GetBaseAnchor(source, sourceDir);
        var (endX, endY, endDir) = AnchorService.GetBaseAnchor(target, targetDir);


        var sourceAnchors = AnchorService.GetAnchors(source);
        var targetAnchors = AnchorService.GetAnchors(target);

        var candidates = new List<(string path, double score)>();

        foreach (var a in sourceAnchors)
        {
            var initialDir = GetInitialDirection(a.dir);

            foreach (var t in targetAnchors)
            {
                if (!IsDirectionCompatible(initialDir, t.dir))
                    continue;

                // NOW you generate BOTH L and Z for this pair
                candidates.Add(BuildL((a.X, a.Y, a.dir), (t.X, t.Y, t.dir)));
                candidates.Add(BuildZ((a.X, a.Y, a.dir), (t.X, t.Y, t.dir)));
            }
        }

        return candidates
            .OrderBy(c => c.score)
            .First()
            .path;
    }

    public static string GetOrthogonalPath(
    IDesignerItem source,
    AnchorDirection sourceDir,
    (double x, double y) mouse)
    {
        var (startX, startY, startDir) = AnchorService.GetBaseAnchor(source, sourceDir);

        var (endX, endY, endDir) = (mouse.x, mouse.y, AnchorDirection.None);

        // Only ONE anchor matters here: the source
        var initialDir = GetInitialDirection(sourceDir);

        var candidates = new List<(string path, double score)>
    {
        BuildL((startX, startY, startDir), (endX, endY, endDir)),
        BuildZ((startX, startY, startDir), (endX, endY, endDir))
    };

        return candidates
            .OrderBy(c => c.score)
            .First()
            .path;
    }

    private static (string path, double score) BuildL(
    (double x, double y, AnchorDirection dir) source,
    (double x, double y, AnchorDirection dir) target)
    {
        var sb = new StringBuilder();

        sb.Append($"M {source.x} {source.y} ");

        // horizontal-first L (you can also invert later if you want smarter scoring)
        double midX = target.x;

        sb.Append($"L {midX} {source.y} ");
        sb.Append($"L {target.x} {target.y}");

        double score = Math.Abs(source.x - target.x) + Math.Abs(source.y - target.y);

        return (sb.ToString(), score);
    }

    private static (string path, double score) BuildZ(
    (double x, double y, AnchorDirection dir) source,
    (double x, double y, AnchorDirection dir) target)
    {
        var sb = new StringBuilder();

        sb.Append($"M {source.x} {source.y} ");

        // first segment respects anchor direction slightly
        double exitX = source.x;
        double exitY = source.y;

        switch (source.dir)
        {
            case AnchorDirection.Left:
                exitX -= Offset;
                break;
            case AnchorDirection.Right:
                exitX += Offset;
                break;
            case AnchorDirection.Top:
                exitY -= Offset;
                break;
            case AnchorDirection.Bottom:
                exitY += Offset;
                break;
        }

        sb.Append($"L {exitX} {exitY} ");

        // Z-corner (orthogonal turn)
        sb.Append($"L {exitX} {target.y} ");
        sb.Append($"L {target.x} {target.y}");

        // score: slightly penalize bends
        double score =
            Math.Abs(source.x - target.x) +
            Math.Abs(source.y - target.y) +
            5; // bend penalty

        return (sb.ToString(), score);
    }
}
