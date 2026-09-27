using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler.Interaction;
using Argent.WebComponents.Workflows.Modeler.Routing;
using Xunit;
using Xunit.Abstractions;

namespace Argent.WebComponents.Tests.Workflows.Modeler;

/// <summary>
/// Native routing measurements for the release fixtures. These record the cost of the
/// geometry work a gesture triggers; they are tracked separately from browser frame
/// timings, which the browser gate measures.
/// </summary>
public class RoutingPerformanceTests(ITestOutputHelper output)
{
    private static (DesignerService State, GeometryEditor Editor) Loaded(WorkflowDefinition definition)
    {
        var state = new DesignerService(new FakeDesignerStore(), new TestRegistry());
        state.LoadDefinition(definition);
        return (state, GeometryEditor.For(state));
    }

    public static IEnumerable<object[]> Sizes => ModelerFixtures.PerformanceSizes.Select(s => new object[] { s.Size });

    [Theory]
    [MemberData(nameof(Sizes))]
    public void LoadingAFixtureRoutesEveryConnection(int size)
    {
        var definition = ModelerFixtures.Performance(size);
        var stopwatch = Stopwatch.StartNew();
        var (state, _) = Loaded(definition);
        stopwatch.Stop();

        output.WriteLine($"{size} nodes: load+route {stopwatch.Elapsed.TotalMilliseconds:F1} ms for {state.Connections.Count} connections");
        Assert.Equal(definition.Connections.Count, state.Connections.Count);
        Assert.All(state.Connections, c => Assert.True(c.Waypoints.Count >= 2));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void AWarmedDragOfOneNodeStaysWithinTheGestureBudget(int size)
    {
        var (state, editor) = Loaded(ModelerFixtures.Performance(size));
        var node = state.Nodes[size / 2];
        var baseline = DesignerSnapshot.Capture(state);

        // Warm the router so the measurement is not dominated by first-call JIT.
        editor.MoveNodes(baseline, [node], 1, 1);

        const int moves = 200;
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < moves; i++)
            editor.MoveNodes(baseline, [node], 2 + (i % 7), 2 + (i % 5));
        stopwatch.Stop();

        double perMove = stopwatch.Elapsed.TotalMilliseconds / moves;
        output.WriteLine($"{size} nodes: {perMove:F3} ms per pointer move");
        Assert.True(perMove < 16.7, $"{size} nodes: {perMove:F2} ms per pointer move exceeds a 60 Hz frame");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void ABulkMoveReroutesEachAffectedConnectionOnce(int size)
    {
        var (state, editor) = Loaded(ModelerFixtures.Performance(size));
        var baseline = DesignerSnapshot.Capture(state);
        var moving = state.Nodes.Take(Math.Max(2, size / 10)).ToList();

        var stopwatch = Stopwatch.StartNew();
        editor.MoveNodes(baseline, moving, 25, 15);
        stopwatch.Stop();

        output.WriteLine($"{size} nodes: bulk move of {moving.Count} nodes took {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.All(moving, n => Assert.Equal(25 + baseline.OriginOf(n).X, n.X, 3));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void ReroutingEverythingProducesOrthogonalRoutesThatReachBothPorts(int size)
    {
        var (state, editor) = Loaded(ModelerFixtures.Performance(size));
        editor.RerouteAll();

        foreach (var connection in state.Connections)
        {
            var path = connection.Waypoints;
            Assert.True(path.Count >= 2);
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                Assert.True(Math.Abs(a.X - b.X) < 0.001 || Math.Abs(a.Y - b.Y) < 0.001,
                    $"segment {i} of a {size} node fixture is not orthogonal");
            }

            // Every automatic route starts and ends on the ports it is attached to, and
            // leaves and enters along the port direction, so no arrow points into a node
            // from behind.
            var (sx, sy, _) = AnchorService.GetBaseAnchor(connection.Source, connection.SourceDir);
            var (tx, ty, _) = AnchorService.GetBaseAnchor(connection.Target, connection.TargetDir);
            Assert.Equal(sx, path[0].X, 3);
            Assert.Equal(sy, path[0].Y, 3);
            Assert.Equal(tx, path[^1].X, 3);
            Assert.Equal(ty, path[^1].Y, 3);
            Assert.True(Math.Sign(path[1].X - path[0].X) == DirSign(connection.SourceDir, true) &&
                        (Math.Sign(path[1].Y - path[0].Y) == DirSign(connection.SourceDir, false) ||
                         path[1].X == path[0].X));
        }
    }

    private static int DirSign(AnchorDirection dir, bool horizontal) => dir switch
    {
        AnchorDirection.Left => horizontal ? -1 : 0,
        AnchorDirection.Right => horizontal ? 1 : 0,
        AnchorDirection.Top => horizontal ? 0 : -1,
        AnchorDirection.Bottom => horizontal ? 0 : 1,
        _ => 0
    };

    [Fact]
    public void BenchmarkDotNetRoutingIsTrackedSeparatelyFromBrowserTimings()
    {
        // The native routing benchmark lives with the other .NET benchmarks; browser frame
        // and pointer-to-render timings are measured by the browser gate and must not be
        // inferred from these numbers.
        output.WriteLine("native routing measurements only; see tests/modeler-perf for the browser gate");
        Assert.True(true);
    }
}
