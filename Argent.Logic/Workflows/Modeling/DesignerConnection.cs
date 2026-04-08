using Argent.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Logic.Workflows.Modeling;

public class DesignerConnection
{
    public required Connection EngineConnection { get; set; }
    public required DesignerNode Source { get; set; }
    public required DesignerNode Target { get; set; }

    // These now define exactly where the wire sits
    public AnchorDirection SourceDir { get; set; }
    public AnchorDirection TargetDir { get; set; }
}
