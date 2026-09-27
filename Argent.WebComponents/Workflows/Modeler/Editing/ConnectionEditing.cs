using Argent.Core.Workflows;
using Argent.WebComponents.Workflows.Modeler.Interaction;
using Argent.WebComponents.Workflows.Modeler.Routing;

namespace Argent.WebComponents.Workflows.Modeler.Editing;

/// <summary>Structural connection operations: inserting a node onto a path and reconnecting ends.</summary>
public static class ConnectionEditing
{
    /// <summary>
    /// Finds the connection a node is being dropped onto, so the canvas can preview the
    /// insertion before the drop commits it.
    /// </summary>
    public static DesignerConnection? FindInsertionTarget(
        DesignerService state, DesignerNode node, double threshold = 15)
    {
        double cx = node.X + node.Width / 2;
        double cy = node.Y + node.Height / 2;
        return state.Connections.FirstOrDefault(connection =>
            connection.Source != node && connection.Target != node &&
            RoutingService.FindNearestSegment(connection.Waypoints, cx, cy, threshold) >= 0);
    }

    /// <summary>
    /// Splits a connection in two with <paramref name="node"/> in the middle.
    ///
    /// The guard travels on the first edge: a condition, default-branch flag or task action
    /// belongs to the outgoing edge of the node that evaluates it, and a gateway only looks
    /// at its own outgoing connections, so moving it to the second edge would change the
    /// behaviour of the workflow. The manual route intent is likewise kept on the first
    /// edge, whose start port has not moved, and is copied rather than shared so the two
    /// connections can never edit each other's constraints.
    /// </summary>
    public static bool SplitConnection(DesignerService state, DesignerNode node, DesignerConnection target)
    {
        if (target.Source == node || target.Target == node) return false;

        var original = target.EngineConnection;
        var first = new DesignerConnection
        {
            EngineConnection = new Connection
            {
                Id = Guid.NewGuid(),
                From = target.Source.NodeData,
                To = node.NodeData,
                Label = original.Label,
                Expression = original.Expression,
                Condition = original.Condition,
                IsDefault = original.IsDefault,
                TaskAction = original.TaskAction,
                Route = target.Route?.Clone()
            },
            Source = target.Source,
            Target = node
        };

        var second = new DesignerConnection
        {
            EngineConnection = new Connection
            {
                Id = Guid.NewGuid(),
                From = node.NodeData,
                To = target.Target.NodeData
            },
            Source = node,
            Target = target.Target
        };

        int index = state.Connections.IndexOf(target);
        state.Connections.Remove(target);
        state.Connections.Insert(Math.Max(0, index), first);
        state.Connections.Insert(Math.Max(0, index) + 1, second);

        var editor = GeometryEditor.For(state);
        editor.RouteConnection(first);
        editor.RouteConnection(second);
        return true;
    }

    /// <summary>Clears the manual route intent so the connection is routed automatically again.</summary>
    public static void ResetRoute(DesignerService state, DesignerConnection connection)
    {
        ElasticRouter.ResetIntent(connection, state.Nodes);
        state.MarkDirty();
    }

    /// <summary>
    /// Builds the connection that replaces a connection whose end was dragged onto a new
    /// node, carrying over everything the user configured on it.
    /// </summary>
    public static DesignerConnection Reconnect(
        DesignerNode source, AnchorDirection sourceDir,
        DesignerNode target, AnchorDirection targetDir,
        DesignerConnection? original)
    {
        var engine = original?.EngineConnection;
        var connection = new DesignerConnection
        {
            EngineConnection = new Connection
            {
                Id = original?.Id ?? Guid.NewGuid(),
                From = source.NodeData,
                To = target.NodeData,
                Label = engine?.Label,
                Expression = engine?.Expression,
                Condition = engine?.Condition,
                IsDefault = engine?.IsDefault ?? false,
                TaskAction = engine?.TaskAction,
                Route = original?.Route?.Clone()
            },
            Source = source,
            Target = target,
            SourceDir = sourceDir,
            TargetDir = targetDir
        };
        return connection;
    }
}
