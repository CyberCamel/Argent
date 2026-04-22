using Argent.Contracts.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Logic.Workflows.Modeling.Routing;

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

    public static (double X, double Y, AnchorDirection dir) GetClosestAnchor(IDesignerItem item, IDesignerItem item2)
    {
        var anchors = GetAnchors(item);
        var targetAnchors = GetAnchors(item2);
        return anchors.OrderBy(a => targetAnchors.Min(t => Math.Sqrt(Math.Pow(a.X - t.X, 2) + Math.Pow(a.Y - t.Y, 2)))).First();
    }

}
