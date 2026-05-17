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

    // These now define exactly where the wire sits
    public AnchorDirection SourceDir { get; set; }
    public AnchorDirection TargetDir { get; set; }

    public bool IsUserRouted { get; set; }
    public List<DesignerWaypoint> Waypoints { get; set; } = [];
    public double BaselineSourceX { get; set; }
    public double BaselineSourceY { get; set; }
    public double BaselineTargetX { get; set; }
    public double BaselineTargetY { get; set; }
    public string PathString { get; set; }
}

public class DesignerWaypoint
{
    public double X { get; set; }
    public double Y { get; set; }
}
