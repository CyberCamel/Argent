namespace Argent.Core.Workers;

/// <summary>
/// A registered worker process. Registration is performed by an administrator; the worker itself
/// only presents the key it was issued and reports what it is. The engine addresses a worker by
/// <see cref="Name"/> alone and never reads <see cref="Runtime"/> or <see cref="Subjects"/> to make
/// a routing decision.
/// </summary>
public class Worker
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique routing key targeted by <c>WorkerActivity.WorkerName</c>. Chosen by the administrator.</summary>
    public string Name { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    /// <summary>
    /// Free-form identification string the client reports on its heartbeat, for example
    /// "Argent Python Worker 0.1". Not an enum on purpose: nothing routes on this value, and a
    /// client library should be free to describe itself however it likes. Null until the client
    /// first reports, which is how an administrator can tell a provisioned worker from one that
    /// has never connected.
    /// </summary>
    public string? Runtime { get; set; }

    public WorkerStatus Status { get; set; } = WorkerStatus.Offline;

    /// <summary>Informational. Reserved for a future push transport; unused by the SQL-backed queue.</summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Hash of the bearer key this worker presents. The plaintext is shown to the administrator
    /// exactly once, when the worker is provisioned or its key is rotated, and is never stored.
    /// </summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>Subjects the client reported it has handlers for, as a JSON string array.</summary>
    public string Subjects { get; set; } = "[]";

    /// <summary>Maximum in-flight claims this worker accepts.</summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>Last reported in-flight task count, from the most recent heartbeat.</summary>
    public int InFlight { get; set; }

    /// <summary>Subject the worker reported it is currently running, for display only.</summary>
    public string? CurrentSubject { get; set; }

    public DateTime? LastHeartbeatAt { get; set; }

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Number of times the API key has been rotated.</summary>
    public int KeyRotations { get; set; }

    /// <summary>Concurrency token.</summary>
    public Guid RowVersion { get; set; }

    public IReadOnlyList<string> GetSubjects() => WorkerRequestSerialization.ReadSubjects(Subjects);

    public void SetSubjects(IEnumerable<string> subjects) => Subjects = WorkerRequestSerialization.WriteSubjects(subjects);
}
