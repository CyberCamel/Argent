using Argent.Models.Attributes;
using Argent.Models.Workflows.Modeler.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows.Activities;

[WorkflowCanvasElement("REST Activity", "cloud_sync", "Server", NodeShape.Rectangle, "Calls a REST service", "node-script")]
public class RestActivity : ServerActivity
{
    [NodeProperty("URL", "The URL of the REST service to call", true, PropertyDataType.Text)]
    public string Url { get; set; } = string.Empty;
    [NodeProperty("Method", "The HTTP method to use", true, PropertyDataType.Text)]
    public string Method { get; set; } = "GET";
    [NodeProperty("Headers", "The HTTP headers to include in the request", false, PropertyDataType.KeyValuePairs)]
    public string ContentType { get; set; } = "application/json";
    public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    [NodeProperty("Body", "The body of the request (for POST, PUT, etc.)", false, PropertyDataType.MultiLineText)]
    public object? Body { get; set; }
    [NodeProperty("Timeout", "The timeout for the REST call in seconds", false, PropertyDataType.Number)]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

}
