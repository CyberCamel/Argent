using Argent.Runtime.Workflows.Modeling;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.WebComponents.Core.Workflows.Modeler.Utils;

public class SnapResult
{
    public double SnappedX { get; set; }
    public double SnappedY { get; set; }

    public DesignerNode? TargetNode { get; set; }

    public SnapAxis? XSnapAxis { get; set; }
    public SnapAxis? YSnapAxis { get; set; }
}

public enum SnapAxis
{
    Start,
    Center,
    End
}
