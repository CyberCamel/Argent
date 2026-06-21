using Argent.Models.Workflows;

namespace Argent.Runtime.Workflows.Modeling;

public class DesignerLane
{
    public Lane Data { get; set; } = new();
    public DesignerPool Pool { get; set; } = null!;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsSelected { get; set; }
}
