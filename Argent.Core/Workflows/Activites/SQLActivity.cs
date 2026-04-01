using Argent.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Activites;

[WorkflowCanvasElement("SQL Activity", "database", "Server", "Executes SQL")]
public class SQLActivity : ServerActivity
{
    public string ConnectionKey { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = [];
    public bool UseTransaction { get; set; } = false;

}
