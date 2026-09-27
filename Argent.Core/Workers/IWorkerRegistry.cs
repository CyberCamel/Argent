namespace Argent.Core.Workers;

/// <summary>Registry of worker identities known to the deployment.</summary>
public interface IWorkerRegistry
{
    Task<Worker?> GetByIdAsync(Guid workerId, CancellationToken ct = default);

    Task<Worker?> GetByNameAsync(string name, CancellationToken ct = default);

    Task<IReadOnlyList<Worker>> ListAsync(CancellationToken ct = default);

    /// <summary>Looks a worker up by API key hash. Returns null when no worker holds that key.</summary>
    Task<Worker?> FindByApiKeyHashAsync(string apiKeyHash, CancellationToken ct = default);

    /// <summary>True when a worker with this name is registered and not disabled.</summary>
    Task<bool> IsRegisteredAsync(string name, CancellationToken ct = default);

    Task<bool> NameIsAvailableAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Provisions a worker on the administrator's behalf and returns the plaintext API key, which
    /// is never stored. Throws when the name is already taken: unlike the removed self-registration
    /// path, a name is never silently taken over.
    /// </summary>
    Task<WorkerProvisioningResult> ProvisionAsync(Worker worker, CancellationToken ct = default);

    /// <summary>
    /// Issues a new key for an existing worker and returns the plaintext. The previous key stops
    /// working immediately, so a client holding it must be reconfigured.
    /// </summary>
    Task<WorkerProvisioningResult> RotateApiKeyAsync(Guid workerId, CancellationToken ct = default);

    /// <summary>
    /// Records what the client reports about itself. Runtime and subjects are overwritten because
    /// they describe the running process, not the registration.
    /// </summary>
    Task<bool> HeartbeatAsync(
        Guid workerId,
        string? runtime,
        IReadOnlyList<string>? subjects,
        int inFlight,
        string? currentSubject,
        CancellationToken ct = default);

    /// <summary>Revokes or restores a worker. A revoked worker stays visible with its history intact.</summary>
    Task SetStatusAsync(Guid workerId, WorkerStatus status, CancellationToken ct = default);

    /// <summary>
    /// Removes a worker registration. The requests it ran are deliberately kept: they are the
    /// record of what a workflow instance was told, and must outlive the process that produced them.
    /// </summary>
    Task<bool> DeleteAsync(Guid workerId, CancellationToken ct = default);

    /// <summary>Flips workers whose last heartbeat is older than <paramref name="staleAfter"/> to Offline. Returns the number changed.</summary>
    Task<int> MarkStaleWorkersOfflineAsync(TimeSpan staleAfter, CancellationToken ct = default);
}
