using Argent.Core.Workflows;
using Argent.Core.Workflows;

namespace Argent.WebComponents.Workflows.Modeler;

public class DesignerNode : IDesignerItem
{
    public required NodeBase NodeData { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();

    private double _width = 160;

    public string Title { get; set; } = "New Node";
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "question";
    public string CssClass { get; set; } = string.Empty;
    public NodeShape Shape { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get => Shape == NodeShape.Circle ? Height : _width; set => _width = value; }
    public double Height { get; set; } = 80;

    public bool IsSelected { get; set; }
}
