using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows;

public class Connection : CanvasElement
{
    public string? Expression { get; set; }
    public required INode From { get; set; }
    public required INode To { get; set; }

}
