using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler.Editing;
using Argent.WebComponents.Workflows.Modeler.Interaction;
using Argent.WebComponents.Workflows.Modeler.Routing;
using Xunit;

namespace Argent.WebComponents.Tests.Workflows.Modeler;

/// <summary>
/// Route intent has to survive the whole round trip: compile, serialize, load, publish,
/// duplicate and reset. Definitions written before route metadata existed must keep
/// loading without a migration.
/// </summary>
public class RoutePersistenceTests
{
    private static (DesignerService State, GeometryEditor Editor, FakeDesignerStore Store) NewDesigner()
    {
        var store = new FakeDesignerStore();
        var state = new DesignerService(store, new TestRegistry());
        return (state, GeometryEditor.For(state), store);
    }

    private static DesignerNode AddNode(DesignerService state, double x, double y, string name = "n") =>
        new()
        {
            NodeData = new UserActivity { Name = name },
            Title = name,
            Shape = NodeShape.Rectangle,
            X = x, Y = y, Width = 160, Height = 80
        };

    [Fact]
    public async Task AManualRouteSurvivesSaveAndReload()
    {
        var (state, _, store) = NewDesigner();
        var a = AddNode(state, 0, 100, "a");
        var b = AddNode(state, 400, 240, "b");
        state.Nodes.Add(a);
        state.Nodes.Add(b);
        var connection = new DesignerConnection
        {
            EngineConnection = new Connection { From = a.NodeData, To = b.NodeData },
            Source = a,
            Target = b
        };
        state.Connections.Add(connection);
        ElasticRouter.Route(connection, state.Nodes);

        // The user drags the vertical run to x = 270 and releases.
        connection.Waypoints[1].X = 270;
        connection.Waypoints[2].X = 270;
        ElasticRouter.CommitIntent(connection, state.Nodes);
        var structure = connection.Route!.Structure;
        var value = connection.Route.Segments.Single().Value;

        state.Compile();
        await state.SaveDraftAsync();

        // A fresh designer over the same stored definition reproduces the same route.
        var reloaded = new DesignerService(store, new TestRegistry());
        reloaded.LoadDefinition(store.LastSaved!);
        var reloadedConnection = reloaded.Connections.Single();

        Assert.Equal(structure, reloadedConnection.Route!.Structure);
        Assert.Equal(value, reloadedConnection.Route.Segments.Single().Value, 3);
        Assert.Contains(reloadedConnection.Waypoints, w => Math.Abs(w.X - value) < 0.001);
    }

    [Fact]
    public void RouteMetadataRoundTripsThroughTheSerializer()
    {
        var route = new ConnectionRoute
        {
            SourceSide = AnchorDirection.Bottom,
            TargetSide = AnchorDirection.Left,
            Structure = "HVHV",
            Segments =
            [
                new RouteSegment { Axis = RouteAxis.Vertical, X = 120, Index = 0 },
                new RouteSegment { Axis = RouteAxis.Horizontal, Y = 340, Index = 0 }
            ]
        };

        var json = JsonSerializer.Serialize(route);
        var restored = JsonSerializer.Deserialize<ConnectionRoute>(json)!;

        Assert.Equal(ConnectionRoute.CurrentVersion, restored.Version);
        Assert.Equal(AnchorDirection.Bottom, restored.SourceSide);
        Assert.Equal(AnchorDirection.Left, restored.TargetSide);
        Assert.Equal("HVHV", restored.Structure);
        Assert.Equal(2, restored.Segments.Count);
        Assert.Equal(120, restored.Segments[0].X);
        Assert.Null(restored.Segments[0].Y);
        Assert.Equal(340, restored.Segments[1].Y);
        Assert.Null(restored.Segments[1].X);
    }

    [Fact]
    public void ADefinitionWithoutRouteMetadataLoadsAsAutomatic()
    {
        var (state, _, _) = NewDesigner();
        var a = new UserActivity { Name = "a" };
        var b = new UserActivity { Name = "b" };

        // A connection as written before route metadata existed.
        var legacy = new Connection { From = a, To = b };
        Assert.Null(legacy.Route);

        state.LoadDefinition(new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata(),
            Nodes = [a, b],
            Connections = [legacy],
            Layouts = new Dictionary<Guid, NodeLayout>
            {
                [a.Id] = new() { X = 0, Y = 100 },
                [b.Id] = new() { X = 400, Y = 240 }
            }
        });

        var connection = state.Connections.Single();
        Assert.Null(connection.Route);
        Assert.False(ElasticRouter.HasIntent(connection));
        Assert.True(connection.Waypoints.Count >= 3);
    }

    [Fact]
    public void ConnectionsReceiveAStableIdentityOnLoad()
    {
        var (state, _, _) = NewDesigner();
        var a = new UserActivity { Name = "a" };
        var b = new UserActivity { Name = "b" };
        var connection = new Connection { From = a, To = b };
        connection.Id = Guid.Empty;   // as if it had been written by an older build

        state.LoadDefinition(new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata(),
            Nodes = [a, b],
            Connections = [connection]
        });

        Assert.NotEqual(Guid.Empty, state.Connections.Single().EngineConnection.Id);
    }

    [Fact]
    public void DuplicatedConnectionsHaveDistinctIdentitiesAndIndependentConstraints()
    {
        var (state, _, _) = NewDesigner();
        var a = AddNode(state, 0, 100, "a");
        var b = AddNode(state, 400, 240, "b");
        state.Nodes.Add(a);
        state.Nodes.Add(b);
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

        var a2 = AddNode(state, 0, 500, "a2");
        var b2 = AddNode(state, 400, 640, "b2");
        state.Nodes.Add(a2);
        state.Nodes.Add(b2);
        var duplicate = connection.CloneFor(a2, b2);
        state.Connections.Add(duplicate);

        Assert.NotEqual(connection.EngineConnection.Id, duplicate.EngineConnection.Id);
        Assert.NotSame(connection.Route, duplicate.Route);
        Assert.Equal(connection.Route!.Structure, duplicate.Route!.Structure);

        // Editing one copy's constraint must not touch the other.
        duplicate.Route!.Segments[0].X = 999;
        Assert.NotEqual(999, connection.Route!.Segments[0].X);
    }

    [Fact]
    public async Task ResetRoutingIsUndoable()
    {
        var (state, _, _) = NewDesigner();
        var a = AddNode(state, 0, 100, "a");
        var b = AddNode(state, 400, 240, "b");
        state.Nodes.Add(a);
        state.Nodes.Add(b);
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

        var gesture = new ModelerInteractionController(state);
        gesture.Begin();
        ConnectionEditing.ResetRoute(state, connection);
        gesture.Commit("Reset routing");
        Assert.Null(connection.Route);

        state.Undo();
        Assert.NotNull(connection.Route);
        Assert.Equal(270, connection.Route!.Segments.Single().Value, 3);
    }

    [Fact]
    public async Task PublishingAWorkflowKeepsTheStoredRoute()
    {
        var (state, _, store) = NewDesigner();
        var a = AddNode(state, 0, 100, "a");
        var b = AddNode(state, 400, 240, "b");
        state.Nodes.Add(a);
        state.Nodes.Add(b);
        var connection = new DesignerConnection
        {
            EngineConnection = new Connection { From = a.NodeData, To = b.NodeData, Label = "yes" },
            Source = a, Target = b
        };
        state.Connections.Add(connection);
        ElasticRouter.Route(connection, state.Nodes);
        connection.Waypoints[1].X = 270;
        connection.Waypoints[2].X = 270;
        ElasticRouter.CommitIntent(connection, state.Nodes);
        var structure = connection.Route!.Structure;
        state.Compile();
        await state.SaveDraftAsync();

        await state.PublishVersionAsync(isMajor: true);

        // The version the store published is the same definition, route and all.
        Assert.Equal(structure, store.Definition!.Connections.Single().Route!.Structure);
        Assert.Equal(270, store.Definition.Connections.Single().Route!.Segments.Single().Value, 3);
        Assert.Equal("yes", state.Connections.Single().EngineConnection.Label);
    }
}
