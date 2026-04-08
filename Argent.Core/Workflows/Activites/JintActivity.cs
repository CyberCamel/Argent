using Argent.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Activites;

[WorkflowCanvasElement("Jint Script", "code", "Server", NodeShape.Rectangle, "An activity that runs server-side JavaScript", "workflow-node node-script")]
public class JintActivity : ServerActivity
{
    
    
    public string Code { get; set; } = string.Empty;

}
