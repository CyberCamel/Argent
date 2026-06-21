using Argent.Models.Workflows;

namespace Argent.Runtime.Workflows.Modeling;

public class DesignerPool
{
    public Pool Data { get; set; } = new();
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsSelected { get; set; }
}
