using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Logic.Workflows.Modeling;

public class DesignerNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string NodeType { get; set; } = default!;
    public string Title { get; set; } = default!;
    public int Height { get; set; } = 80;
    public int Width { get; set; } = 150;
    public double X { get; set; }
    public double Y { get; set; }
    public string Icon { get; set; } = "bi-gear"; // Bootstrap Icon
}
