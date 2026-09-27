using Argent.Core.Workflows;
using Argent.Core.Workflows.Modeler;

namespace Argent.WebComponents.Workflows.Modeler;

public class DesignerConnection
{
    public required Connection EngineConnection { get; set; }
    public required DesignerNode Source { get; set; }
    public required DesignerNode Target { get; set; }

    public AnchorDirection SourceDir { get; set; }
    public AnchorDirection TargetDir { get; set; }

    /// <summary>Calculated geometry. Rebuilt from <see cref="Route"/> on every change.</summary>
    public List<DesignerWaypoint> Waypoints { get; set; } = [];

    /// <summary>
    /// True when a stored intent exists but cannot currently be honoured, so an automatic
    /// route is being shown instead. Set by the router when it computes the path; the
    /// canvas only reads it, because rendering must never recompute geometry.
    /// </summary>
    public bool RouteSuspended { get; set; }

    /// <summary>The user's persisted layout intent, if any. Never holds calculated waypoints.</summary>
    public ConnectionRoute? Route
    {
        get => EngineConnection.Route;
        set => EngineConnection.Route = value;
    }

    public Guid Id => EngineConnection.Id;

    public List<DesignerWaypoint> CopyWaypoints() =>
        [.. Waypoints.Select(w => new DesignerWaypoint { X = w.X, Y = w.Y })];

    /// <summary>
    /// Creates an independent copy of this connection pointing at <paramref name="source"/>
    /// and <paramref name="target"/>, with a fresh identity and a copy of the route intent.
    /// Used by copy/paste, duplicate and workflow split so duplicated connections never
    /// share an id or a constraint list.
    /// </summary>
    public DesignerConnection CloneFor(DesignerNode source, DesignerNode target)
    {
        var clone = new DesignerConnection
        {
            EngineConnection = new Connection
            {
                Id = Guid.NewGuid(),
                From = source.NodeData,
                To = target.NodeData,
                Expression = EngineConnection.Expression,
                Condition = EngineConnection.Condition,
                IsDefault = EngineConnection.IsDefault,
                Label = EngineConnection.Label,
                TaskAction = EngineConnection.TaskAction,
                Route = Route?.Clone()
            },
            Source = source,
            Target = target,
            SourceDir = SourceDir,
            TargetDir = TargetDir,
            Waypoints = CopyWaypoints()
        };
        return clone;
    }
}

public class DesignerWaypoint
{
    public double X { get; set; }
    public double Y { get; set; }
}
