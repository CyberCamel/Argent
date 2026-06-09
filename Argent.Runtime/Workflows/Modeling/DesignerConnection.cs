using Argent.Models.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Runtime.Workflows.Modeling;

public class DesignerConnection
{
    public required Connection EngineConnection { get; set; }
    public required DesignerNode Source { get; set; }
    public required DesignerNode Target { get; set; }

    public AnchorDirection SourceDir { get; set; }
    public AnchorDirection TargetDir { get; set; }

    // The path is defined by an ordered list of waypoints.
    // Each consecutive pair (wp[i], wp[i+1]) MUST share either X or Y,
    // guaranteeing all bends are exactly 90 degrees.
    public List<DesignerWaypoint> Waypoints { get; set; } = [];
}

public class DesignerWaypoint
{
    public double X { get; set; }
    public double Y { get; set; }
}
