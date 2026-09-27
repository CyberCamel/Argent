namespace Argent.Core.Workers;

/// <summary>
/// The outbound half of the worker contract — how a pending request reaches a worker process.
/// The first implementation claims from the SQL-backed <see cref="IWorkerRequestQueue"/>; a
/// broker-backed implementation can replace it without touching the node handler or the clients.
/// The inbound half (results) is expressed by <see cref="IWorkerRequestQueue.CompleteAsync"/>,
/// because a result has to be written transactionally alongside the work item it releases.
/// </summary>
public interface IWorkerTransport
{
    /// <summary>Identifier reported in logs and admin views to distinguish transports.</summary>
    string Name { get; }

    Task<IReadOnlyList<WorkerRequest>> ClaimAsync(
        string workerName,
        int maxItems,
        TimeSpan lease,
        CancellationToken ct = default);

    Task<bool> RenewAsync(Guid requestId, TimeSpan lease, CancellationToken ct = default);
}
