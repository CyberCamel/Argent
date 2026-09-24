using Argent.Core.Workflows;

namespace Argent.WebComponents.Workflows.Modeler;

public class DesignerConnection
{
    public required Connection EngineConnection { get; set; }
    public required DesignerNode Source { get; set; }
    public required DesignerNode Target { get; set; }

    public AnchorDirection SourceDir { get; set; }
    public AnchorDirection TargetDir { get; set; }

    public List<DesignerWaypoint> Waypoints { get; set; } = [];
}

public class DesignerWaypoint
{
    public double X { get; set; }
    public double Y { get; set; }
}
