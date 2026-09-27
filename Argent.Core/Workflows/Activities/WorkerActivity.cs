using Argent.Core.Attributes;
using Argent.Core.Workflows.Modeler.Enums;

namespace Argent.Core.Workflows.Activities;

/// <summary>
/// Delegates a unit of work to an external worker process. The engine enqueues a request addressed
/// to <see cref="WorkerName"/> and waits for the worker to respond; it never learns what the work is.
/// </summary>
[WorkflowCanvasElement("Worker Activity", "precision_manufacturing", "Server", NodeShape.Rectangle, "Delegates work to an external worker process", "workflow-node node-worker")]
public class WorkerActivity : ServerActivity
{
    [NodeProperty("Worker", "Name of the registered worker that should run this task", true, PropertyDataType.Text)]
    public string WorkerName { get; set; } = string.Empty;

    [NodeProperty("Subject", "Task identifier the worker has configured a handler for", true, PropertyDataType.Text)]
    public string Subject { get; set; } = string.Empty;

    [NodeProperty("Parameters", "Key/value parameters passed to the worker. Values support {{variable}} placeholders", false, PropertyDataType.KeyValuePairs)]
    public List<WorkerParameter> Parameters { get; set; } = [];

    [NodeProperty("Timeout", "Seconds the worker may run before the request fails", false, PropertyDataType.Number)]
    public int TimeoutSeconds { get; set; } = 900;

    [NodeProperty("Attempts", "How many times the request may be delivered before it fails", false, PropertyDataType.Number)]
    public int MaxAttempts { get; set; } = 1;

    [NodeProperty("Wait for worker", "Wait until the worker registers instead of failing immediately when it is unknown", false, PropertyDataType.Boolean)]
    public bool WaitForWorker { get; set; }
}
