using System;
using System.Collections.Generic;
using System.Linq;
using Argent.Core.Workflows;
using Argent.WebComponents.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler.Routing;
using Xunit;

namespace Argent.WebComponents.Tests.Workflows.Modeler;

public class AutoRoutingTests
{
    private sealed class Item(double x, double y, double width = 100, double height = 60) : IDesignerItem
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public double X { get; set; } = x;
        public double Y { get; set; } = y;
        public double Width { get; set; } = width;
        public double Height { get; set; } = height;
    }

    [Fact]
    public void ReversedPortsRouteAroundBothShapes()
    {
        var source = new Item(300, 100);
        var target = new Item(100, 100);
        var path = RoutingService.AutoRoute(source, AnchorDirection.Right, target, AnchorDirection.Left);

        Assert.Equal((400d, 130d), (path[0].X, path[0].Y));
        Assert.Equal((100d, 130d), (path[^1].X, path[^1].Y));
        Assert.All(Pairs(path), pair => Assert.True(ClearOf(pair, source) && ClearOf(pair, target)));
        Assert.True(path.Count >= 6);
    }

    [Fact]
    public void InterveningNodeIsAvoided()
    {
        var source = new Item(0, 100);
        var target = new Item(400, 100);
        var obstacle = new Item(210, 80);
        var path = RoutingService.AutoRoute(source, AnchorDirection.Right, target, AnchorDirection.Left,
            obstacles: [source, target, obstacle]);

        Assert.All(Pairs(path), pair => Assert.True(ClearOf(pair, obstacle)));
        Assert.All(Pairs(path), pair => Assert.True(pair.Item1.X == pair.Item2.X || pair.Item1.Y == pair.Item2.Y));
        Assert.Equal((100d, 130d), (path[0].X, path[0].Y));
        Assert.Equal((400d, 130d), (path[^1].X, path[^1].Y));
    }

    public static IEnumerable<object[]> PortPairs =>
        from source in new[] { AnchorDirection.Left, AnchorDirection.Right, AnchorDirection.Top, AnchorDirection.Bottom }
        from target in new[] { AnchorDirection.Left, AnchorDirection.Right, AnchorDirection.Top, AnchorDirection.Bottom }
        select new object[] { source, target };

    [Theory]
    [MemberData(nameof(PortPairs))]
    public void EveryPortPairAvoidsStaggeredObstacles(AnchorDirection sourceDir, AnchorDirection targetDir)
    {
        var source = new Item(0, 0);
        var target = new Item(600, 250);
        IDesignerItem[] nodes = [source, target,
            new Item(240, 80, 100, 120), new Item(360, 220, 80, 170), new Item(360, -120, 80, 160)];
        var path = RoutingService.AutoRoute(source, sourceDir, target, targetDir, obstacles: nodes);
        var (sx, sy, _) = AnchorService.GetBaseAnchor(source, sourceDir);
        var (tx, ty, _) = AnchorService.GetBaseAnchor(target, targetDir);
        Assert.Equal((sx, sy), (path[0].X, path[0].Y));
        Assert.Equal((tx, ty), (path[^1].X, path[^1].Y));
        AssertClear(path, nodes);
    }

    [Fact]
    public void RepeatedRoutesUseTheCurrentObstaclePositions()
    {
        var source = new Item(0, 100);
        var target = new Item(700, 100);
        var obstacle = new Item(320, 80, 100, 140);
        IDesignerItem[] nodes = [source, target, obstacle];
        for (int i = 0; i < 30; i++)
        {
            obstacle.Y = i % 2 == 0 ? 80 : -40;
            target.Y = 100 + i;
            var path = RoutingService.AutoRoute(source, AnchorDirection.Right, target, AnchorDirection.Left,
                obstacles: nodes);
            AssertClear(path, nodes);
        }
    }

    [Fact]
    public void OverlappingObstacleMarginsAndDistantNodesDoNotBlockValidDetours()
    {
        var source = new Item(0, 100);
        var target = new Item(700, 100);
        var nodes = new List<IDesignerItem> { source, target,
            new Item(290, 60, 60, 180), new Item(360, -50, 60, 180) };
        var random = new Random(1234);
        for (int i = 0; i < 80; i++)
            nodes.Add(new Item(random.NextDouble() * 2500, 500 + random.NextDouble() * 2000));
        var path = RoutingService.AutoRoute(source, AnchorDirection.Right, target, AnchorDirection.Left,
            obstacles: nodes);
        AssertClear(path, nodes);
    }

    private static void AssertClear(List<DesignerWaypoint> path, IEnumerable<IDesignerItem> nodes)
    {
        Assert.All(Pairs(path), pair =>
        {
            Assert.True(pair.Item1.X == pair.Item2.X || pair.Item1.Y == pair.Item2.Y);
            Assert.All(nodes, node => Assert.True(ClearOf(pair, node)));
        });
    }

    private static IEnumerable<(DesignerWaypoint, DesignerWaypoint)> Pairs(List<DesignerWaypoint> path) =>
        path.Zip(path.Skip(1));

    private static bool ClearOf((DesignerWaypoint A, DesignerWaypoint B) segment, IDesignerItem item)
    {
        var (a, b) = segment;
        if (a.Y == b.Y)
            return !(a.Y > item.Y && a.Y < item.Y + item.Height &&
                Math.Max(a.X, b.X) > item.X && Math.Min(a.X, b.X) < item.X + item.Width);
        return !(a.X > item.X && a.X < item.X + item.Width &&
            Math.Max(a.Y, b.Y) > item.Y && Math.Min(a.Y, b.Y) < item.Y + item.Height);
    }
}
