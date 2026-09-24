using System.Diagnostics.Metrics;

namespace Argent.Runtime.Workflows.Execution;

public static class WorkflowMeter
{
    public static readonly Meter Engine = new("Argent.WorkflowEngine", "1.0.0");

    public static readonly Counter<int> ItemsClaimed = Engine.CreateCounter<int>(
        "argent.engine.items_claimed",
        description: "Number of work items claimed per batch");

    public static readonly Counter<int> TokensMoved = Engine.CreateCounter<int>(
        "argent.engine.tokens_moved",
        description: "Number of tokens moved to target nodes");

    public static readonly Histogram<double> HandlerDurationMs = Engine.CreateHistogram<double>(
        "argent.engine.handler_duration_ms",
        unit: "ms",
        description: "Handler execution duration");
}
