using Argent.Core.Attributes;
using Argent.Core.Workflows.Modeler.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Activites;

[WorkflowCanvasElement("SQL Activity", "database", "Server", NodeShape.Rectangle, "Executes SQL", "workflow-node node-script")]
public class SQLActivity : ServerActivity
{
    [NodeProperty("Connection Key", "The key to retrieve the database connection string from the configuration.", true, PropertyDataType.Text)]
    public string ConnectionKey { get; set; } = string.Empty;
    [NodeProperty("Query", "The SQL query to execute.", true, PropertyDataType.MultiLineText)]
    public string Query { get; set; } = string.Empty;
    [NodeProperty("Parameters", "The parameters for the SQL query.", false, PropertyDataType.KeyValuePairs)]
    public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    [NodeProperty("Use Transaction", "Whether to use a transaction for the SQL execution.", false, PropertyDataType.Boolean)]
    public bool UseTransaction { get; set; } = false;

}
