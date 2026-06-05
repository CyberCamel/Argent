using Argent.Models.Attributes;
using Argent.Models.Workflows.Modeler.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows.Activities;

[WorkflowCanvasElement("Jint Script", "code", "Server", NodeShape.Rectangle, "An activity that runs server-side JavaScript", "workflow-node node-script")]
public class JintActivity : ServerActivity
{

    [NodeProperty("Code", "The JavaScript code to execute", true, PropertyDataType.Code)]
    public string Code { get; set; } = string.Empty;

    [NodeProperty("Parameters", "Optional parameters to pass to the script", false, PropertyDataType.KeyValuePairs)]
    public Dictionary<string, object> Parameters { get; set; } = [];
    [NodeProperty("Return Variable", "The name of the variable to store the result in", false, PropertyDataType.Text)]
    public string ReturnVariable { get; set; } = string.Empty;

}
