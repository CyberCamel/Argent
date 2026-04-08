using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Logic.Workflows.Modeling;

public class ConnectionDraft
{
    public required DesignerNode Source { get; set; }
    public AnchorDirection SourceDir { get; set; }
    public double MouseX { get; set; }
    public double MouseY { get; set; }
}
