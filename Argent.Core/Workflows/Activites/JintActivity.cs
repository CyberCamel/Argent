using Argent.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Activites;

[WorkflowCanvasElement("Jint Script", "code", "Server", "An activity that runs server-side JavaScript")]
public class JintActivity : ServerActivity
{
    
    
    public string Code { get; set; } = string.Empty;

}
