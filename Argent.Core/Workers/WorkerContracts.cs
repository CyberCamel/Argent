namespace Argent.Core.Workers;

/// <summary>
/// Request and response shapes for the worker API. These are the wire contract shared by
/// Argent.Web and every worker client library, and are mirrored by
/// <c>schemas/argent-worker-protocol.schema.json</c>.
///
/// There is deliberately no register endpoint here. Workers are provisioned by an administrator,
/// who is shown the API key once; a client only ever presents the key it was issued.
/// </summary>
public record WorkerCreateRequest(
    string Name,
    string? DisplayName = null,
    int Concurrency = 1);

/// <summary>Carries the plaintext API key. It exists only in this response and is never stored.</summary>
public record WorkerProvisionedResponse(
    Guid WorkerId,
    string Name,
    string ApiKey,
    int Concurrency,
    DateTime ServerTimeUtc);

/// <summary>
/// What a client reports about itself. Sent on every heartbeat, which is the only channel a client
/// has: there is no registration call for it to introduce itself through.
/// </summary>
public record WorkerHeartbeatRequest(
    /// <summary>
    /// Free-form identification, for example "Argent Python Worker 0.1". Overwrites the stored
    /// value, so a client upgraded to a new version is visible as such.
    /// </summary>
    string? Runtime = null,

    /// <summary>Subjects this client currently has handlers for. Overwrites the stored list.</summary>
    IReadOnlyList<string>? Subjects = null,

    int InFlight = 0,
    string? CurrentSubject = null);

public record WorkerHeartbeatResponse(
    string Status,
    string Name,
    int Concurrency,
    DateTime ServerTimeUtc);

public record WorkerClaimRequest(int MaxItems = 1);

/// <summary>A unit of work handed to a worker. <see cref="Id"/> is stable across delivery attempts and is the idempotency key.</summary>
public record WorkerTaskPayload(
    Guid Id,
    Guid InstanceId,
    Guid TokenId,
    Guid NodeId,
    string WorkerName,
    string Subject,
    IReadOnlyDictionary<string, string> Parameters,
    int Attempt,
    int MaxAttempts,
    int TimeoutSeconds,
    DateTime LeaseExpiresAt);

public record WorkerCompleteRequest(
    bool Succeeded,
    IReadOnlyDictionary<string, string>? Outputs = null,
    string? Error = null);

public record WorkerRenewLeaseRequest(int? LeaseSeconds = null);

public record WorkerRequestStatusResponse(
    Guid Id,
    string State,
    int Attempt,
    DateTime? LeaseExpiresAt,
    IReadOnlyDictionary<string, string> Outputs,
    string? Error);

/// <summary>What the client is, as the server currently understands it. Returned by <c>GET /api/workers/me</c>.</summary>
public record WorkerIdentityResponse(
    Guid WorkerId,
    string Name,
    string Status,
    int Concurrency,
    string? Runtime,
    IReadOnlyList<string> Subjects);
