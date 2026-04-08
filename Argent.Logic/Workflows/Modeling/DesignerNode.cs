using Argent.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Logic.Workflows.Modeling;

using Argent.Core.Attributes;
using Argent.Core.Workflows;
using Argent.Logic.Workflows.Modeling;

public class DesignerNode
{
    public required INode NodeData { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();

    // Metadata for the UI
    public string Title { get; set; } = "New Node";
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "question";
    public string CssClass { get; set; } = string.Empty;
    public NodeShape Shape { get; set; }

    // Transformation
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get => Shape == NodeShape.Circle ? Height : 160; set ; }
    public double Height { get; set; } = 80;

    public bool IsSelected { get; set; }
    public List<AnchorDirection> AvailableAnchors { get; set; } = [AnchorDirection.Left, AnchorDirection.Right];
}
