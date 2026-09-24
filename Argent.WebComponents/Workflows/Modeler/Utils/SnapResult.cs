using Argent.WebComponents.Workflows.Modeler;

namespace Argent.WebComponents.Workflows.Modeler.Utils;

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
