using System.Diagnostics.Metrics;

namespace Argent.Runtime.Workers;

public static class WorkerMeter
{
    public static readonly Meter Workers = new("Argent.Workers", "1.0.0");

    public static readonly Counter<int> RequestsEnqueued = Workers.CreateCounter<int>(
        "argent.workers.requests_enqueued",
        description: "Worker requests created by the workflow engine");

    public static readonly Counter<int> RequestsClaimed = Workers.CreateCounter<int>(
        "argent.workers.requests_claimed",
        description: "Worker requests claimed by a worker process");

    public static readonly Counter<int> RequestsSucceeded = Workers.CreateCounter<int>(
        "argent.workers.requests_succeeded",
        description: "Worker requests completed successfully by a worker");

    public static readonly Counter<int> RequestsFailed = Workers.CreateCounter<int>(
        "argent.workers.requests_failed",
        description: "Worker requests reported as failed by a worker");

    public static readonly Counter<int> RequestsTimedOut = Workers.CreateCounter<int>(
        "argent.workers.requests_timed_out",
        description: "Worker requests that exhausted their attempts or budget");

    public static readonly Counter<int> LeasesExpired = Workers.CreateCounter<int>(
        "argent.workers.leases_expired",
        description: "Claimed worker requests requeued after their lease expired");

    public static readonly Histogram<double> RequestDurationMs = Workers.CreateHistogram<double>(
        "argent.workers.request_duration_ms",
        unit: "ms",
        description: "Time from enqueue to terminal result, including time spent waiting for a worker");
}
