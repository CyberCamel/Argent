namespace Argent.Core.Workers;

/// <summary>Result of a delivery attempt. Claims are granted one at a time so callers can stop early.</summary>
public record WorkerClaimResult(IReadOnlyList<WorkerRequest> Requests, int Reclaimed, int TimedOut);

/// <summary>
/// The worker request queue: the engine's side of the enqueue-and-wait contract.
/// Every operation is safe to call from either the engine host or the web host.
/// </summary>
public interface IWorkerRequestQueue
{
    /// <summary>
    /// Enqueues a request, or returns the request already queued for the same token and node.
    /// Idempotent per <c>(TokenId, NodeId)</c> so a retried work item cannot enqueue twice.
    /// </summary>
    Task<WorkerRequest> EnqueueAsync(WorkerRequest request, CancellationToken ct = default);

    Task<WorkerRequest?> GetAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>The request belonging to a token, if one has been enqueued. Backs the handler's resume path.</summary>
    Task<WorkerRequest?> GetForTokenAsync(Guid tokenId, CancellationToken ct = default);

    /// <summary>
    /// Atomically claims up to <paramref name="maxItems"/> pending requests addressed to this worker,
    /// extending each request's lease. Requests whose attempts are exhausted are timed out instead,
    /// which fails their owning work item.
    /// </summary>
    Task<WorkerClaimResult> ClaimAsync(
        Guid workerId,
        string workerName,
        int maxItems,
        TimeSpan lease,
        CancellationToken ct = default);

    /// <summary>Extends the lease on a request the worker currently holds.</summary>
    Task<bool> RenewLeaseAsync(Guid requestId, Guid workerId, TimeSpan lease, CancellationToken ct = default);

    /// <summary>
    /// Records a terminal result and releases the owning work item back to Pending so the engine
    /// resumes the token. A worker may only complete a request it currently holds.
    /// </summary>
    Task<WorkerRequest?> CompleteAsync(
        Guid requestId,
        Guid workerId,
        bool succeeded,
        IReadOnlyDictionary<string, string>? outputs,
        string? errorMessage,
        CancellationToken ct = default);

    /// <summary>Operator-initiated cancellation. Fails the owning work item with the supplied reason.</summary>
    Task<WorkerRequest?> CancelAsync(Guid requestId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Sweeps claimed requests whose lease has expired: returns them to Pending while attempts remain,
    /// times them out otherwise. Both outcomes release or fail the owning work item.
    /// </summary>
    Task<WorkerRecoveryResult> RecoverExpiredLeasesAsync(CancellationToken ct = default);

    /// <summary>Times out claimed requests that outlived their budget even while their lease is being renewed.</summary>
    Task<int> TimeOutOverdueRequestsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<WorkerRequest>> QueryAsync(WorkerRequestQuery query, CancellationToken ct = default);
}

public record WorkerRecoveryResult(int Requeued, int TimedOut);

public record WorkerRequestQuery(
    Guid? InstanceId = null,
    string? WorkerName = null,
    WorkerRequestState? State = null,
    int Limit = 50);
