using System;
using System.Collections.Generic;
using System.Linq;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler.Routing;
using Xunit;

namespace Argent.WebComponents.Tests.Workflows.Modeler;

/// <summary>
/// The elastic router. A manually bent connection must keep the user's route when its
/// endpoints move, fall back to a valid automatic route when the intent cannot be honoured,
/// and return to the stored route once it becomes valid again.
/// </summary>
public class ElasticRoutingTests
{
    private static DesignerNode Node(double x, double y, double w = 100, double h = 60) =>
        new()
        {
            NodeData = new UserActivity(),
            Shape = NodeShape.Rectangle,
            X = x, Y = y, Width = w, Height = h
        };

    private static DesignerConnection Connect(DesignerNode source, DesignerNode target) => new()
    {
        EngineConnection = new Connection { From = source.NodeData, To = target.NodeData },
        Source = source,
        Target = target,
        SourceDir = AnchorDirection.Right,
        TargetDir = AnchorDirection.Left
    };

    /// <summary>
    /// A source and a target at different heights, so the automatic route has a vertical
    /// run between them that the user can drag.
    /// </summary>
    private static (DesignerNode Source, DesignerNode Target, List<DesignerNode> Obstacles, DesignerConnection Connection) Bent()
    {
        var source = Node(0, 100);
        var target = Node(400, 240);
        var connection = Connect(source, target);
        var obstacles = new List<DesignerNode> { source, target };
        ElasticRouter.Route(connection, obstacles);
        return (source, target, obstacles, connection);
    }

    private static void DragVerticalRun(DesignerConnection connection, double delta)
    {
        // The vertical run of an H-V-H route is the middle segment.
        connection.Waypoints[1].X += delta;
        connection.Waypoints[2].X += delta;
        ElasticRouter.CommitIntent(connection, []);
    }

    [Fact]
    public void AutomaticRouteHasABendToDrag()
    {
        var (_, _, _, connection) = Bent();
        Assert.Equal(4, connection.Waypoints.Count);
        Assert.Equal("HVH", new string([.. Segments(connection.Waypoints)]));
    }

    [Fact]
    public void MovingAnEndpointStretchesTheBentSegmentInsteadOfErasingIt()
    {
        var (_, target, obstacles, connection) = Bent();
        DragVerticalRun(connection, -90);
        Assert.NotNull(connection.Route);
        Assert.Single(connection.Route!.Segments);
        var constraint = connection.Route.Segments.Single().Value;
        Assert.Equal(270, constraint, 3);

        // The far endpoint moves; the bent line stays exactly where the user put it.
        target.X = 800;
        ElasticRouter.Route(connection, obstacles);

        Assert.True(ElasticRouter.HasIntent(connection));
        Assert.Contains(connection.Waypoints, w => Math.Abs(w.X - constraint) < 0.001);
        Assert.Equal((800d, 270d), (connection.Waypoints[^1].X, connection.Waypoints[^1].Y));
        AssertClear(connection.Waypoints, obstacles);
    }

    [Fact]
    public void BothEndpointsMovingTranslatesThePathAndItsConstraints()
    {
        var (source, target, obstacles, connection) = Bent();
        DragVerticalRun(connection, -90);
        var constraint = connection.Route!.Segments.Single().Value;
        var original = connection.CopyWaypoints();

        source.X += 60; source.Y += 30;
        target.X += 60; target.Y += 30;
        bool translated = ElasticRouter.TryTranslate(connection, 60, 30, obstacles, out bool valid);

        Assert.True(translated);
        Assert.True(valid);
        Assert.Equal(original.Count, connection.Waypoints.Count);
        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].X + 60, connection.Waypoints[i].X, 3);
            Assert.Equal(original[i].Y + 30, connection.Waypoints[i].Y, 3);
        }
        Assert.Equal(constraint + 60, connection.Route!.Segments.Single().Value, 3);
    }

    [Fact]
    public void ATranslatedRouteBlockedByAnObstacleRollsBackAndKeepsTheIntent()
    {
        var (source, target, obstacles, connection) = Bent();
        DragVerticalRun(connection, -90);
        var constraint = connection.Route!.Segments.Single().Value;

        // A node lands right where the translated route would run.
        source.X += 60;
        target.X += 60;
        var blocker = Node(300, 60, 40, 140);
        obstacles.Add(blocker);

        bool translated = ElasticRouter.TryTranslate(connection, 60, 0, obstacles, out bool valid);

        Assert.False(translated);
        Assert.False(valid);
        // The translation was undone, so the intent is exactly where the user left it.
        Assert.Equal(constraint, connection.Route!.Segments.Single().Value, 3);

        ElasticRouter.Route(connection, obstacles);
        AssertClear(connection.Waypoints, obstacles);
    }

    [Fact]
    public void AnUnsatisfiableRouteIsSuspendedAndRestoredWhenItBecomesValidAgain()
    {
        var (_, _, obstacles, connection) = Bent();
        DragVerticalRun(connection, -90);
        var structure = connection.Route!.Structure;
        var value = connection.Route.Segments.Single().Value;

        // A node on the bent line makes the stored intent unsatisfiable.
        obstacles.Add(Node(230, 20, 60, 200));
        Assert.False(ElasticRouter.Route(connection, obstacles));
        AssertClear(connection.Waypoints, obstacles);
        Assert.NotNull(connection.Route);
        Assert.Equal(structure, connection.Route!.Structure);
        Assert.Equal(value, connection.Route.Segments.Single().Value, 3);

        // Removing the obstacle brings the user's route back.
        obstacles.RemoveAt(obstacles.Count - 1);
        Assert.True(ElasticRouter.Route(connection, obstacles));
        Assert.Equal(structure, connection.Route!.Structure);
        Assert.Contains(connection.Waypoints, w => Math.Abs(w.X - value) < 0.001);
    }

    [Fact]
    public void AValidPreferredPortIsNotSwappedOnEverySmallMovement()
    {
        var (_, target, obstacles, connection) = Bent();
        DragVerticalRun(connection, -90);
        var preferredSource = connection.SourceDir;
        var preferredTarget = connection.TargetDir;

        // A movement small enough that automatic direction picking would pick another side.
        target.Y = 268;
        ElasticRouter.Route(connection, obstacles);

        Assert.Equal(preferredSource, connection.SourceDir);
        Assert.Equal(preferredTarget, connection.TargetDir);
        Assert.Equal(preferredSource, connection.Route!.SourceSide);
        Assert.Equal(preferredTarget, connection.Route!.TargetSide);
    }

    [Fact]
    public void ResettingRoutingClearsTheIntent()
    {
        var (_, _, obstacles, connection) = Bent();
        DragVerticalRun(connection, -90);
        Assert.True(ElasticRouter.HasIntent(connection));

        ElasticRouter.ResetIntent(connection, obstacles);

        Assert.False(ElasticRouter.HasIntent(connection));
        Assert.Null(connection.Route);
        AssertClear(connection.Waypoints, obstacles);
    }

    [Fact]
    public void AConnectionWithoutIntentIsRoutedAutomatically()
    {
        var (_, _, obstacles, connection) = Bent();
        Assert.False(ElasticRouter.HasIntent(connection));
        Assert.Null(connection.Route);
        AssertClear(connection.Waypoints, obstacles);
    }

    [Fact]
    public void RepeatedRoutingOfABentConnectionDoesNotDrift()
    {
        var (_, _, obstacles, connection) = Bent();
        DragVerticalRun(connection, -90);
        var expected = connection.CopyWaypoints();

        for (int i = 0; i < 25; i++)
        {
            ElasticRouter.Route(connection, obstacles);
            Assert.Equal(expected.Count, connection.Waypoints.Count);
            for (int k = 0; k < expected.Count; k++)
            {
                Assert.Equal(expected[k].X, connection.Waypoints[k].X, 6);
                Assert.Equal(expected[k].Y, connection.Waypoints[k].Y, 6);
            }
        }
    }

    [Fact]
    public void EveryPortPairProducesAValidRouteAndSurvivesAManualBend()
    {
        foreach (var sourceDir in Sides)
        foreach (var targetDir in Sides)
        {
            var source = Node(0, 0, 120, 80);
            var target = Node(600, 300, 120, 80);
            var obstacles = new List<DesignerNode>
            {
                source, target, Node(240, 120, 80, 160), Node(380, -40, 80, 120), Node(300, 420, 200, 80)
            };
            var connection = Connect(source, target);
            connection.SourceDir = sourceDir;
            connection.TargetDir = targetDir;
            ElasticRouter.Route(connection, obstacles);
            AssertClear(connection.Waypoints, obstacles);

            // Bend an interior line and confirm the intent is still honoured and valid.
            var waypoints = connection.Waypoints;
            if (waypoints.Count >= 4)
            {
                DragVerticalRun(connection, 37);
                Assert.True(ElasticRouter.HasIntent(connection));
                ElasticRouter.Route(connection, obstacles);
                AssertClear(connection.Waypoints, obstacles);
            }
        }
    }

    [Fact]
    public void MovingAPoolStyleTranslationOfAManualRouteStaysClearOfObstacles()
    {
        var (source, target, obstacles, connection) = Bent();
        DragVerticalRun(connection, -90);

        source.X += 40; source.Y += 40;
        target.X += 40; target.Y += 40;
        Assert.True(ElasticRouter.TryTranslate(connection, 40, 40, obstacles, out bool valid));
        Assert.True(valid);
        AssertClear(connection.Waypoints, obstacles);
    }

    [Fact]
    public void AManualBendIsRejectedWhenItWouldReverseTheArrowAtTheTarget()
    {
        var (_, target, obstacles, connection) = Bent();

        // Drag the vertical run past the target port: the approach would reverse.
        DragVerticalRun(connection, 400);
        var constraint = connection.Route!.Segments.Single().Value;
        Assert.False(ElasticRouter.Route(connection, obstacles));
        AssertClear(connection.Waypoints, obstacles);
        // The intent is kept so it comes back when the geometry allows it again.
        Assert.True(ElasticRouter.HasIntent(connection));

        target.X += 400;
        Assert.True(ElasticRouter.Route(connection, obstacles));
        Assert.Equal("HVH", connection.Route!.Structure);
        Assert.Contains(connection.Waypoints, w => Math.Abs(w.X - constraint) < 0.001);
        AssertClear(connection.Waypoints, obstacles);
    }

    [Fact]
    public void RouteValidityCanBeCheckedForClampingAManualEdit()
    {
        var (_, _, obstacles, connection) = Bent();
        Assert.True(ElasticRouter.IsRouteValid(connection, obstacles));

        // Push the vertical run beyond the target port.
        connection.Waypoints[1].X += 400;
        connection.Waypoints[2].X += 400;
        Assert.False(ElasticRouter.IsRouteValid(connection, obstacles));
    }

    private static readonly AnchorDirection[] Sides =
        [AnchorDirection.Left, AnchorDirection.Right, AnchorDirection.Top, AnchorDirection.Bottom];

    private static IEnumerable<char> Segments(List<DesignerWaypoint> waypoints) =>
        waypoints.Zip(waypoints.Skip(1))
            .Select(pair => Math.Abs(pair.First.Y - pair.Second.Y) < 0.001 ? 'H' : 'V');

    private static void AssertClear(List<DesignerWaypoint> path, IEnumerable<IDesignerItem> nodes)
    {
        Assert.True(path.Count >= 2);
        for (int i = 0; i < path.Count - 1; i++)
        {
            var a = path[i];
            var b = path[i + 1];
            Assert.True(Math.Abs(a.X - b.X) < 0.001 || Math.Abs(a.Y - b.Y) < 0.001,
                $"segment {i} is not orthogonal");
            foreach (var node in nodes)
                Assert.True(ClearOf(a, b, node), $"segment {i} crosses a node");
        }
    }

    private static bool ClearOf(DesignerWaypoint a, DesignerWaypoint b, IDesignerItem item)
    {
        if (Math.Abs(a.Y - b.Y) < 0.001)
            return !(a.Y > item.Y && a.Y < item.Y + item.Height &&
                Math.Max(a.X, b.X) > item.X && Math.Min(a.X, b.X) < item.X + item.Width);
        return !(a.X > item.X && a.X < item.X + item.Width &&
            Math.Max(a.Y, b.Y) > item.Y && Math.Min(a.Y, b.Y) < item.Y + item.Height);
    }
}
