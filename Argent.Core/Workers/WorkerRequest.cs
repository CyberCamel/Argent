namespace Argent.Core.Workers;

/// <summary>
/// One unit of work delegated by the workflow engine to an external worker process.
/// The engine writes the request-side columns; the worker API writes the claim, lease and result columns.
/// </summary>
public class WorkerRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InstanceId { get; set; }
    public Guid TokenId { get; set; }

    /// <summary>The work item to re-pend when this request reaches a terminal state, so the token resumes.</summary>
    public Guid WorkItemId { get; set; }

    public Guid NodeId { get; set; }

    /// <summary>Target worker name. Matches <see cref="Worker.Name"/>.</summary>
    public string WorkerName { get; set; } = string.Empty;

    /// <summary>Opaque task identifier chosen by the workflow author. The worker decides what it means.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Author-supplied key/value parameters, as a JSON object of string values.</summary>
    public string Parameters { get; set; } = "{}";

    public WorkerRequestState State { get; set; } = WorkerRequestState.Pending;

    public short Priority { get; set; }

    /// <summary>Number of delivery attempts made so far. Incremented on each claim.</summary>
    public byte Attempt { get; set; }

    public byte MaxAttempts { get; set; } = 1;

    public DateTime? LeaseExpiresAt { get; set; }

    public Guid? ClaimedByWorkerId { get; set; }

    public DateTime? ClaimedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>Worker-supplied result, as a JSON object of string values. Becomes process variables on success.</summary>
    public string? Outputs { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Author-supplied execution budget. A claim that outlives this without completing is failed by the recovery pass.</summary>
    public int TimeoutSeconds { get; set; } = 900;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Concurrency token.</summary>
    public Guid RowVersion { get; set; }

    public IReadOnlyDictionary<string, string> GetParameters() => WorkerRequestSerialization.ReadKeyValues(Parameters);

    public IReadOnlyDictionary<string, string> GetOutputs() => WorkerRequestSerialization.ReadKeyValues(Outputs);

    public bool IsTerminal => State is WorkerRequestState.Succeeded
        or WorkerRequestState.Failed
        or WorkerRequestState.TimedOut
        or WorkerRequestState.Cancelled;
}
