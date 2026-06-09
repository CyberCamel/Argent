using Argent.Contracts.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Runtime.Workflows.Modeling.Routing;

public static class AnchorService
{

    public static List<(double X, double Y, AnchorDirection dir)> GetAnchors(IDesignerItem item)
    {
        return [GetBaseAnchor(item, AnchorDirection.Left),
            GetBaseAnchor(item,AnchorDirection.Right),
            GetBaseAnchor(item, AnchorDirection.Top),
            GetBaseAnchor(item, AnchorDirection.Bottom)
            ];
    }


    public static (double X, double Y, AnchorDirection dir) GetBaseAnchor(IDesignerItem node, AnchorDirection dir)
    {
        return dir switch
        {
            AnchorDirection.Left => (node.X, node.Y + (node.Height / 2), dir),
            AnchorDirection.Right => (node.X + node.Width, node.Y + (node.Height / 2), dir),
            AnchorDirection.Top => (node.X + (node.Width / 2), node.Y, dir),
            AnchorDirection.Bottom => (node.X + (node.Width / 2), node.Y + node.Height, dir),
            _ => (node.X, node.Y, dir)
        };
    }

    public static (double X, double Y, AnchorDirection dir) GetClosestAnchor(IDesignerItem item, (double X, double Y) point)
    {
        var anchors = GetAnchors(item);
        return anchors.OrderBy(a => Math.Sqrt(Math.Pow(a.X - point.X, 2) + Math.Pow(a.Y - point.Y, 2))).First();
    }

    public static (double X, double Y, AnchorDirection dir) GetBestTargetAnchor(
        IDesignerItem target, (double X, double Y) mouse, AnchorDirection sourceDir)
    {
        var anchors = GetAnchors(target);
        (double X, double Y, AnchorDirection dir)? best = null;
        double bestScore = double.MaxValue;

        foreach (var a in anchors)
        {
            double dist = Math.Sqrt(Math.Pow(a.X - mouse.X, 2) + Math.Pow(a.Y - mouse.Y, 2));
            double dirPenalty = AnchorDirectionPenalty(sourceDir, a.dir);
            double score = dist + dirPenalty;

            if (score < bestScore)
            {
                bestScore = score;
                best = a;
            }
        }

        return best ?? anchors.First();
    }

    private static double AnchorDirectionPenalty(AnchorDirection sourceDir, AnchorDirection targetDir)
    {
        bool sH = sourceDir is AnchorDirection.Left or AnchorDirection.Right;
        bool tH = targetDir is AnchorDirection.Left or AnchorDirection.Right;

        if (sH && tH)
            return sourceDir != targetDir ? 0 : 30;

        if (!sH && !tH)
            return sourceDir != targetDir ? 0 : 30;

        return 15;
    }

    public static (double X, double Y, AnchorDirection dir) GetClosestAnchor(
    IDesignerItem item,
    IDesignerItem target)
    {
        var anchors = GetAnchors(item);
        var targetAnchors = GetAnchors(target);

        (double X, double Y, AnchorDirection dir)? best = null;
        double bestDistance = double.MaxValue;

        foreach (var a in anchors)
        {
            foreach (var t in targetAnchors)
            {

                double dx = a.X - t.X;
                double dy = a.Y - t.Y;
                double dist = dx * dx + dy * dy;

                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    best = a;
                }
            }
        }

        return best ?? anchors.First();
    }

}
