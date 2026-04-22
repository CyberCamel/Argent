using Argent.Contracts.Workflows;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Argent.Logic.Workflows.Modeling.Routing;

public class RoutingService
{
    private const double Padding = 25;

    // Use this for standard auto-routing
    public static string GetOrthogonalPath(IDesignerItem source, IDesignerItem target)
    {
        var start = AnchorService.GetClosestAnchor(source, target);
        var end = AnchorService.GetClosestAnchor(target, source);
        return GeneratePath(start, end, source, target);
    }

    // NEW: Use this when you want to force specific directions (Fixes your re-route!)
    public static string GetOrthogonalPath(IDesignerItem source, AnchorDirection sourceDir, IDesignerItem target, AnchorDirection targetDir)
    {
        var start = AnchorService.GetBaseAnchor(source, sourceDir);
        var end = AnchorService.GetBaseAnchor(target, targetDir);
        return GeneratePath(start, end, source, target);
    }

    // For dragging to mouse
    public static string GetOrthogonalPath(IDesignerItem source, AnchorDirection sourceDir, (double x, double y) mouse)
    {
        var start = AnchorService.GetBaseAnchor(source, sourceDir);
        var end = (mouse.x, mouse.y, AnchorDirection.None);
        return GeneratePath(start, end, source, null);
    }

    private static string GeneratePath((double x, double y, AnchorDirection dir) source,
                                     (double x, double y, AnchorDirection dir) target,
                                     IDesignerItem sourceNode,
                                     IDesignerItem? targetNode)
    {
        StringBuilder path = new StringBuilder();
        path.Append($"M {source.x.ToString(CultureInfo.InvariantCulture)} {source.y.ToString(CultureInfo.InvariantCulture)} ");

        // 1. Calculate preferred 1-bend "elbow"
        double elbowX = (source.dir == AnchorDirection.Left || source.dir == AnchorDirection.Right) ? target.x : source.x;
        double elbowY = (source.dir == AnchorDirection.Left || source.dir == AnchorDirection.Right) ? source.y : target.y;

        // 2. Intersection Check: Is the target "behind" the exit direction?
        bool collision = source.dir switch
        {
            AnchorDirection.Right => target.x < source.x + Padding,
            AnchorDirection.Left => target.x > source.x - Padding,
            AnchorDirection.Bottom => target.y < source.y + Padding,
            AnchorDirection.Top => target.y > source.y - Padding,
            _ => false
        };

        if (collision)
        {
            // 2 Bends (Z/S-Shape) to go around the node
            double exitX = source.x;
            double exitY = source.y;

            if (source.dir == AnchorDirection.Right) exitX += Padding;
            else if (source.dir == AnchorDirection.Left) exitX -= Padding;
            else if (source.dir == AnchorDirection.Top) exitY -= Padding;
            else if (source.dir == AnchorDirection.Bottom) exitY += Padding;

            path.Append($"L {exitX.ToString(CultureInfo.InvariantCulture)} {exitY.ToString(CultureInfo.InvariantCulture)} ");

            if (source.dir == AnchorDirection.Left || source.dir == AnchorDirection.Right)
                path.Append($"L {exitX.ToString(CultureInfo.InvariantCulture)} {target.y.ToString(CultureInfo.InvariantCulture)} ");
            else
                path.Append($"L {target.x.ToString(CultureInfo.InvariantCulture)} {exitY.ToString(CultureInfo.InvariantCulture)} ");
        }
        else
        {
            // 1 Bend (The Red Line)
            path.Append($"L {elbowX.ToString(CultureInfo.InvariantCulture)} {elbowY.ToString(CultureInfo.InvariantCulture)} ");
        }

        path.Append($"L {target.x.ToString(CultureInfo.InvariantCulture)} {target.y.ToString(CultureInfo.InvariantCulture)}");
        return path.ToString();
    }
}
