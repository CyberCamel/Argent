namespace Argent.Core.Workers;

/// <summary>Lifecycle of a single delegated unit of work.</summary>
public enum WorkerRequestState : byte
{
    /// <summary>Queued and waiting for a worker to claim it.</summary>
    Pending = 0,

    /// <summary>Claimed by a worker and running under a lease. The lease may expire and return it to Pending.</summary>
    Claimed = 1,

    Succeeded = 2,
    Failed = 3,

    /// <summary>All attempts were consumed without a result. The owning work item is failed so the token stops waiting.</summary>
    TimedOut = 4,

    /// <summary>Cancelled by an operator. The owning work item is failed with the cancellation reason.</summary>
    Cancelled = 5
}
