using Argent.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Activites;

[WorkflowCanvasElement("REST Activity", "cloud_sync", "Server", "Calls a REST service")]
public class RestActivity : ServerActivity
{
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    public object? Body { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

}
