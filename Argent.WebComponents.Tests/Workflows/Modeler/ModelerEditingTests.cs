using System;
using System.Collections.Generic;
using System.Linq;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.BoundaryEvents;
using Argent.Core.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler.Editing;
using Argent.WebComponents.Workflows.Modeler.Interaction;
using Argent.WebComponents.Workflows.Modeler.Routing;
using Xunit;

namespace Argent.WebComponents.Tests.Workflows.Modeler;

/// <summary>
/// Editing operations that are easy to get subtly wrong: inserting a node into a
/// connection, reconnecting an endpoint, copy/paste identity remapping, alignment, and the
/// pool and space tools.
/// </summary>
public class ModelerEditingTests
{
    private static (DesignerService State, GeometryEditor Editor) NewDesigner()
    {
        var state = new DesignerService(new FakeDesignerStore(), new TestRegistry());
        return (state, GeometryEditor.For(state));
    }

    private static DesignerNode Add(DesignerService state, double x, double y, string name = "n") =>
        new()
        {
            NodeData = new UserActivity { Name = name },
            Title = name,
            Shape = NodeShape.Rectangle,
            X = x, Y = y, Width = 160, Height = 80
        };

    private static void AddAll(DesignerService state, params DesignerNode[] nodes)
    {
        foreach (var node in nodes) state.Nodes.Add(node);
    }

    [Fact]
    public void InsertingANodeOntoAConnectionKeepsTheGuardOnTheFirstEdge()
    {
        var (state, _) = NewDesigner();
        var gateway = new DesignerNode
        {
            NodeData = new ExclusiveGateway { Name = "split" },
            Title = "gateway",
            Shape = NodeShape.Diamond,
            X = 0, Y = 100, Width = 60, Height = 60
        };
        var target = Add(state, 400, 100, "task");
        AddAll(state, gateway, target);

        var condition = new Argent.Core.Forms.Components.Configuration.ExpressionCondition
        {
            Expression = "amount > 10"
        };
        var connection = new DesignerConnection
        {
            EngineConnection = new Connection
            {
                From = gateway.NodeData,
                To = target.NodeData,
                Label = "expensive",
                IsDefault = false,
                Condition = condition,
                TaskAction = new TaskActionPresentation { Label = "Approve" }
            },
            Source = gateway,
            Target = target
        };
        state.Connections.Add(connection);
        ElasticRouter.Route(connection, state.Nodes);

        var inserted = Add(state, 200, 100, "inserted");
        state.Nodes.Add(inserted);
        Assert.True(ConnectionEditing.SplitConnection(state, inserted, connection));

        var first = state.Connections.Single(c => c.Target == inserted);
        var second = state.Connections.Single(c => c.Source == inserted);

        // A gateway only inspects its own outgoing edges, so the guard stays on the first.
        Assert.Equal("expensive", first.EngineConnection.Label);
        Assert.Same(condition, first.EngineConnection.Condition);
        Assert.Equal("Approve", first.EngineActionLabel());
        Assert.Null(second.EngineConnection.Condition);
        Assert.Null(second.EngineConnection.Label);
        Assert.NotEqual(first.EngineConnection.Id, second.EngineConnection.Id);
    }

    [Fact]
    public void SplittingCopiesRatherThanSharesTheRouteIntent()
    {
        var (state, _) = NewDesigner();
        var a = Add(state, 0, 100, "a");
        var b = Add(state, 400, 240, "b");
        AddAll(state, a, b);
        var connection = new DesignerConnection
        {
            EngineConnection = new Connection { From = a.NodeData, To = b.NodeData },
            Source = a, Target = b
        };
        state.Connections.Add(connection);
        ElasticRouter.Route(connection, state.Nodes);
        connection.Waypoints[1].X = 270;
        connection.Waypoints[2].X = 270;
        ElasticRouter.CommitIntent(connection, state.Nodes);

        var inserted = Add(state, 200, 170, "mid");
        state.Nodes.Add(inserted);
        ConnectionEditing.SplitConnection(state, inserted, connection);

        var first = state.Connections.Single(c => c.Target == inserted);
        Assert.NotNull(first.Route);
        Assert.NotSame(connection.Route, first.Route);
        Assert.Equal(270, first.Route!.Segments.Single().Value, 3);
    }

    [Fact]
    public void ReconnectingAnEndpointKeepsTheMetadataAndCanBeUndone()
    {
        var (state, _) = NewDesigner();
        var a = Add(state, 0, 100, "a");
        var b = Add(state, 400, 240, "b");
        var c = Add(state, 400, 500, "c");
        AddAll(state, a, b, c);
        var connection = new DesignerConnection
        {
            EngineConnection = new Connection
            {
                From = a.NodeData, To = b.NodeData, Label = "keep me", IsDefault = true
            },
            Source = a, Target = b
        };
        state.Connections.Add(connection);
        ElasticRouter.Route(connection, state.Nodes);
        connection.Waypoints[1].X = 270;
        connection.Waypoints[2].X = 270;
        ElasticRouter.CommitIntent(connection, state.Nodes);
        var originalId = connection.EngineConnection.Id;

        // Drag the target endpoint onto c: the connection is removed while dragging, so the
        // gesture is the transaction that has to restore it.
        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        state.Connections.Remove(connection);
        var reconnected = ConnectionEditing.Reconnect(a, AnchorDirection.Right, c, AnchorDirection.Top, connection);
        state.Connections.Add(reconnected);
        State(state, reconnected);
        gesture.Commit("Reconnect connection");

        Assert.Single(state.Connections);
        Assert.Equal("keep me", reconnected.EngineConnection.Label);
        Assert.True(reconnected.EngineConnection.IsDefault);
        Assert.Equal(originalId, reconnected.EngineConnection.Id);
        Assert.NotNull(reconnected.Route);

        state.Undo();
        Assert.Single(state.Connections);
        Assert.Same(b, state.Connections[0].Target);
        Assert.Equal("keep me", state.Connections[0].EngineConnection.Label);
        Assert.Equal(270, state.Connections[0].Route!.Segments.Single().Value, 3);
    }

    [Fact]
    public void CancellingAReconnectLosesNoMetadata()
    {
        var (state, _) = NewDesigner();
        var a = Add(state, 0, 100, "a");
        var b = Add(state, 400, 240, "b");
        AddAll(state, a, b);
        var connection = new DesignerConnection
        {
            EngineConnection = new Connection
            {
                From = a.NodeData, To = b.NodeData, Label = "keep me", IsDefault = true,
                TaskAction = new TaskActionPresentation { Label = "Approve" }
            },
            Source = a, Target = b
        };
        state.Connections.Add(connection);
        ElasticRouter.Route(connection, state.Nodes);
        connection.Waypoints[1].X = 270;
        connection.Waypoints[2].X = 270;
        ElasticRouter.CommitIntent(connection, state.Nodes);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        state.Connections.Remove(connection);   // the endpoint was detached
        gesture.Cancel();

        var restored = Assert.Single(state.Connections);
        Assert.Equal("keep me", restored.EngineConnection.Label);
        Assert.True(restored.EngineConnection.IsDefault);
        Assert.Equal("Approve", restored.EngineConnection.TaskAction?.Label);
        Assert.Equal(270, restored.Route!.Segments.Single().Value, 3);
    }

    [Fact]
    public void CopyPasteRemapsIdentitiesAndInternalReferences()
    {
        var (state, _) = NewDesigner();
        var a = Add(state, 0, 100, "a");
        var b = Add(state, 400, 240, "b");
        var child = new DesignerNode
        {
            NodeData = new TimerBoundaryEvent { ParentNodeId = b.NodeData.Id },
            Title = "timer",
            Shape = NodeShape.Circle,
            X = 460, Y = 70, Width = 40, Height = 40
        };
        AddAll(state, a, b, child);

        var connection = new DesignerConnection
        {
            EngineConnection = new Connection { From = a.NodeData, To = b.NodeData, Label = "internal" },
            Source = a, Target = b
        };
        state.Connections.Add(connection);
        ElasticRouter.Route(connection, state.Nodes);
        connection.Waypoints[1].X = 200;
        connection.Waypoints[2].X = 200;
        ElasticRouter.CommitIntent(connection, state.Nodes);

        var clipboard = new ModelerClipboard();
        clipboard.Copy(state, [a, b, child]);
        var (pasted, pastedConnections) = clipboard.Paste(state);

        Assert.Equal(3, pasted.Count);
        var newIds = pasted.Select(n => n.NodeData.Id).ToHashSet();
        Assert.Equal(3, newIds.Count);
        Assert.Empty(newIds.Intersect([a.NodeData.Id, b.NodeData.Id, child.NodeData.Id]));

        // The boundary event now points at the copied parent, not the original one.
        var pastedChild = pasted.Single(n => n.NodeData is TimerBoundaryEvent);
        var pastedParent = pasted.Single(n => n.Title == "b");
        Assert.Equal(pastedParent.NodeData.Id, ((TimerBoundaryEvent)pastedChild.NodeData).ParentNodeId);

        // The internal connection was rebuilt with a new identity and its own route copy.
        var pastedConnection = Assert.Single(pastedConnections);
        Assert.NotEqual(connection.EngineConnection.Id, pastedConnection.EngineConnection.Id);
        Assert.Equal("internal", pastedConnection.EngineConnection.Label);
        // The original stays; only the internal edge gained a copy.
        Assert.Equal(2, state.Connections.Count);
        Assert.NotNull(pastedConnection.Route);
    }

    [Fact]
    public void PastingIsOneUndoStep()
    {
        var (state, _) = NewDesigner();
        var a = Add(state, 0, 100, "a");
        AddAll(state, a);
        var clipboard = new ModelerClipboard();
        clipboard.Copy(state, [a]);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        clipboard.Paste(state);
        gesture.Commit("Paste");
        Assert.Equal(2, state.Nodes.Count);

        state.Undo();
        Assert.Single(state.Nodes);
    }

    [Theory]
    [InlineData(AlignMode.Left)]
    [InlineData(AlignMode.CenterHorizontal)]
    [InlineData(AlignMode.Right)]
    [InlineData(AlignMode.Top)]
    [InlineData(AlignMode.CenterVertical)]
    [InlineData(AlignMode.Bottom)]
    public void AlignmentLinesUpTheSelection(AlignMode mode)
    {
        var (state, _) = NewDesigner();
        var a = Add(state, 0, 0, "a");
        var b = Add(state, 120, 60, "b");
        var c = Add(state, 300, 20, "c");
        AddAll(state, a, b, c);
        state.SelectNodes([a, b, c], a);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        Assert.True(AlignmentService.Align(state, gesture.Baseline, mode));
        gesture.Commit("Align selection");

        switch (mode)
        {
            case AlignMode.Left:
                Assert.Equal(new[] { 0d, 0d, 0d }, new[] { a.X, b.X, c.X });
                break;
            case AlignMode.Right:
                // The widest node's right edge is 300 + 160.
                Assert.Equal(new[] { 300d, 300d, 300d }, new[] { a.X, b.X, c.X });
                break;
            case AlignMode.Top:
                Assert.Equal(new[] { 0d, 0d, 0d }, new[] { a.Y, b.Y, c.Y });
                break;
            case AlignMode.Bottom:
                // The lowest node's bottom edge is 60 + 80.
                Assert.Equal(new[] { 60d, 60d, 60d }, new[] { a.Y, b.Y, c.Y });
                break;
            case AlignMode.CenterHorizontal:
                Assert.Equal((a.X + a.Width / 2), (b.X + b.Width / 2), 3);
                Assert.Equal((b.X + b.Width / 2), (c.X + c.Width / 2), 3);
                break;
            case AlignMode.CenterVertical:
                Assert.Equal((a.Y + a.Height / 2), (b.Y + b.Height / 2), 3);
                Assert.Equal((b.Y + b.Height / 2), (c.Y + c.Height / 2), 3);
                break;
        }

        state.Undo();
        Assert.Equal(0, a.X);
        Assert.Equal(60, b.Y);
    }

    [Fact]
    public void DistributionSpacesTheMiddleNodeEvenly()
    {
        var (state, _) = NewDesigner();
        var a = Add(state, 0, 0, "a");
        var b = Add(state, 400, 0, "b");
        var c = Add(state, 900, 0, "c");
        AddAll(state, a, b, c);
        state.SelectNodes([a, b, c], a);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        Assert.True(AlignmentService.Distribute(state, gesture.Baseline, DistributeMode.Horizontal));
        gesture.Commit("Distribute selection");

        // Equal gaps between the outer nodes, whatever the original spacing was.
        Assert.Equal(b.X - (a.X + a.Width), c.X - (b.X + b.Width), 3);
    }

    [Fact]
    public void MovingAPoolMovesItsLanesAndNodesAndKeepsTheResultIdenticalAtAnyZoom()
    {
        foreach (double zoom in new[] { 0.25, 0.5, 1.0, 2.0 })
        {
            var (state, editor) = NewDesigner();
            var pool = state.AddPool(0, 0, 800, 400);
            var a = Add(state, 120, 60, "a");
            state.Nodes.Add(a);

            var gesture = new ModelerInteractionController(state);
            gesture.Begin();
            editor.MovePool(gesture.Baseline, pool, 40, 25);
            gesture.CommitSilently();

            Assert.Equal(40, pool.X, 6);
            Assert.Equal(25, pool.Y, 6);
            // Lanes start at the pool's 40 unit header offset and travel with it.
            Assert.Equal(80, state.Lanes[0].X, 6);
            Assert.Equal(25, state.Lanes[0].Y, 6);
            Assert.Equal(160, a.X, 6);
            Assert.Equal(85, a.Y, 6);
        }
    }

    [Fact]
    public void RepeatedPoolMovesDoNotDrift()
    {
        var (state, editor) = NewDesigner();
        var pool = state.AddPool(0, 0, 800, 400);
        var a = Add(state, 120, 60, "a");
        state.Nodes.Add(a);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        for (int i = 1; i <= 15; i++) editor.MovePool(gesture.Baseline, pool, 5 * i, 3 * i);
        gesture.CommitSilently();

        Assert.Equal(75, pool.X, 6);
        Assert.Equal(45, pool.Y, 6);
        Assert.Equal(195, a.X, 6);
        Assert.Equal(105, a.Y, 6);
    }

    [Fact]
    public void TheSpaceToolStopsCompressingAtTheMinimumPoolSize()
    {
        var (state, editor) = NewDesigner();
        state.AddPool(0, 0, 800, 400);
        var pool = state.Pools[0];

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        // A cut inside the pool compresses it; pushing far past zero must not go negative.
        editor.ApplySpaceTool(gesture.Baseline, 400, 0, isHorizontal: true, -5000);
        gesture.CommitSilently();

        Assert.Equal(GeometryEditor.PoolMinWidth, pool.Width, 6);
        Assert.Equal(0, pool.X, 6);
    }

    [Fact]
    public void TheSpaceToolMovesWholeContentAndOneUndoRestoresTheLayout()
    {
        var (state, editor) = NewDesigner();
        var pool = state.AddPool(0, 0, 800, 400);
        var a = Add(state, 120, 60, "a");
        state.Nodes.Add(a);

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        // Cut to the left of everything, so all of it moves right.
        editor.ApplySpaceTool(gesture.Baseline, -50, 0, isHorizontal: true, 60);
        gesture.Commit("Adjust space");

        Assert.Equal(60, pool.X, 6);
        Assert.Equal(180, a.X, 6);
        Assert.Equal(100, state.Lanes[0].X, 6);

        state.Undo();
        Assert.Equal(0, pool.X, 6);
        Assert.Equal(120, a.X, 6);
        Assert.Equal(40, state.Lanes[0].X, 6);
    }

    [Fact]
    public void ResizingAPoolKeepsItsLanesConsistent()
    {
        var (state, editor) = NewDesigner();
        state.AddPool(0, 0, 800, 400);
        var pool = state.Pools[0];

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        editor.ResizePool(pool, resizeWidth: true, resizeHeight: true, 200, 100);
        gesture.Commit("Resize pool");

        Assert.Equal(1000, pool.Width, 6);
        Assert.Equal(500, pool.Height, 6);
        Assert.All(state.Lanes, l => Assert.Equal(pool.Width - 40, l.Width, 6));
        Assert.Equal(pool.Height, state.Lanes.Sum(l => l.Height), 3);
    }

    [Fact]
    public void TheLaneDividerNeverCollapsesEitherLane()
    {
        var (state, editor) = NewDesigner();
        state.AddPool(0, 0, 800, 400);
        var top = state.Lanes[0];
        var bottom = state.Lanes[1];

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        // Far past the top of the pool: compression stops at the minimum.
        editor.ResizeLaneDivider(top, bottom, -1000);
        Assert.Equal(GeometryEditor.LaneMinHeight, top.Height, 6);
        Assert.Equal(400 - GeometryEditor.LaneMinHeight, bottom.Height, 6);

        // And far past the bottom: the other lane stops at its minimum instead.
        editor.ResizeLaneDivider(top, bottom, 10_000);
        gesture.CommitSilently();
        Assert.Equal(400 - GeometryEditor.LaneMinHeight, top.Height, 6);
        Assert.Equal(GeometryEditor.LaneMinHeight, bottom.Height, 6);
        // The two lanes still exactly fill the pool.
        Assert.Equal(400, top.Height + bottom.Height, 3);
    }

    [Fact]
    public void MovingAnUnrelatedNodeOnlyReroutesConnectorsItActuallyTouches()
    {
        var (state, editor) = NewDesigner();
        var a = Add(state, 0, 100, "a");
        var b = Add(state, 400, 240, "b");
        var far = Add(state, 3000, 2000, "far");
        AddAll(state, a, b, far);
        var near = new DesignerConnection
        {
            EngineConnection = new Connection { From = a.NodeData, To = b.NodeData },
            Source = a, Target = b
        };
        state.Connections.Add(near);
        ElasticRouter.Route(near, state.Nodes);
        var untouched = near.CopyWaypoints();

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        editor.MoveNodes(gesture.Baseline, [far], -10, -10);
        gesture.CommitSilently();

        Assert.Equal(untouched.Count, near.Waypoints.Count);
        for (int i = 0; i < untouched.Count; i++)
        {
            Assert.Equal(untouched[i].X, near.Waypoints[i].X, 6);
            Assert.Equal(untouched[i].Y, near.Waypoints[i].Y, 6);
        }
    }

    private static void State(DesignerService state, DesignerConnection connection) =>
        state.Select(connection);
}

internal static class DesignerConnectionTestExtensions
{
    public static string? EngineActionLabel(this DesignerConnection connection) =>
        connection.EngineConnection.TaskAction?.Label;
}
