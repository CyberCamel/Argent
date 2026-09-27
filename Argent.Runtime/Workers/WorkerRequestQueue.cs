using Argent.Core.Workers;
using Argent.Core.Workflows.Execution;
using Argent.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Argent.Runtime.Workers;

/// <summary>
/// The SQL-backed worker request queue. Requests are claimed with the same
/// <c>ROWLOCK, READPAST</c> pattern the workflow engine uses for work items, so a worker can
/// never claim work another worker is already holding.
/// </summary>
public class WorkerRequestQueue : IWorkerRequestQueue, IWorkerTransport
{
    private readonly IDbContextFactory<ArgentDbContext> _contextFactory;
    private readonly string _connectionString;
    private readonly ILogger<WorkerRequestQueue> _logger;

    public WorkerRequestQueue(
        IDbContextFactory<ArgentDbContext> contextFactory,
        string connectionString,
        ILogger<WorkerRequestQueue> logger)
    {
        _contextFactory = contextFactory;
        _connectionString = connectionString;
        _logger = logger;
    }

    public string Name => "sql-queue";

    public async Task<WorkerRequest> EnqueueAsync(WorkerRequest request, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        // Idempotent per (TokenId, NodeId): a work item that is retried after an engine fault
        // re-enters the handler, and it must adopt the request already in flight rather than
        // enqueue a second copy the worker would run twice.
        var existing = await db.WorkerRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TokenId == request.TokenId && r.NodeId == request.NodeId, ct);

        if (existing != null)
            return existing;

        request.Id = request.Id == Guid.Empty ? Guid.NewGuid() : request.Id;
        request.RowVersion = Guid.NewGuid();
        db.WorkerRequests.Add(request);
        await db.SaveChangesAsync(ct);

        WorkerMeter.RequestsEnqueued.Add(1);
        _logger.LogInformation(
            "Worker request {RequestId} enqueued for worker '{Worker}' subject '{Subject}' (token {TokenId}, node {NodeId})",
            request.Id, request.WorkerName, request.Subject, request.TokenId, request.NodeId);

        return request;
    }

    public async Task<WorkerRequest?> GetAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.WorkerRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct);
    }

    public async Task<WorkerRequest?> GetForTokenAsync(Guid tokenId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.WorkerRequests.AsNoTracking().FirstOrDefaultAsync(r => r.TokenId == tokenId, ct);
    }

    public async Task<WorkerClaimResult> ClaimAsync(
        Guid workerId,
        string workerName,
        int maxItems,
        TimeSpan lease,
        CancellationToken ct = default)
    {
        maxItems = Math.Clamp(maxItems, 1, 32);

        var claimedIds = await ClaimRawAsync(workerId, workerName, maxItems, lease, ct);

        var requests = new List<WorkerRequest>();
        if (claimedIds.Count > 0)
        {
            await using var db = await _contextFactory.CreateDbContextAsync(ct);
            requests = await db.WorkerRequests
                .AsNoTracking()
                .Where(r => claimedIds.Contains(r.Id))
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.CreatedAt)
                .ToListAsync(ct);
        }

        if (requests.Count > 0)
        {
            WorkerMeter.RequestsClaimed.Add(requests.Count);
            _logger.LogInformation(
                "Worker '{Worker}' claimed {Count} request(s): {Requests}",
                workerName, requests.Count, string.Join(", ", requests.Select(r => r.Id)));
        }

        return new WorkerClaimResult(requests, 0, 0);
    }

    public async Task<IReadOnlyList<WorkerRequest>> ClaimAsync(
        string workerName,
        int maxItems,
        TimeSpan lease,
        CancellationToken ct = default)
    {
        var result = await ClaimAsync(Guid.Empty, workerName, maxItems, lease, ct);
        return result.Requests;
    }

    public Task<bool> RenewAsync(Guid requestId, TimeSpan lease, CancellationToken ct = default)
        => RenewLeaseAsync(requestId, Guid.Empty, lease, ct);

    private async Task<List<Guid>> ClaimRawAsync(
        Guid workerId,
        string workerName,
        int maxItems,
        TimeSpan lease,
        CancellationToken ct)
    {
        // Same shape as WorkClaimer: take update locks on the candidate rows with READPAST so a
        // concurrent claim of the same rows is skipped rather than blocked on, then flip them to
        // Claimed and hand back the ids. Every column the SET clause assigns must appear in the
        // CTE's select list, or SQL Server rejects the statement as an invalid column.
        const string sql = @"
            WITH claim_cte AS (
                SELECT TOP (@MaxItems)
                       Id, State, ClaimedByWorkerId, ClaimedAt,
                       Attempt, MaxAttempts, LeaseExpiresAt, RowVersion
                FROM WorkerRequests WITH (ROWLOCK, READPAST)
                WHERE WorkerName = @WorkerName AND State = 0
                ORDER BY Priority DESC, CreatedAt
            )
            UPDATE claim_cte
            SET State = 1,
                ClaimedByWorkerId = @WorkerId,
                ClaimedAt = GETUTCDATE(),
                Attempt = Attempt + 1,
                LeaseExpiresAt = DATEADD(SECOND, @LeaseSeconds, GETUTCDATE()),
                RowVersion = NEWID()
            OUTPUT INSERTED.Id;";

        var ids = new List<Guid>(maxItems);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MaxItems", maxItems);
        cmd.Parameters.AddWithValue("@WorkerName", workerName);
        cmd.Parameters.AddWithValue("@LeaseSeconds", (int)Math.Ceiling(lease.TotalSeconds));
        cmd.Parameters.Add("@WorkerId", SqlDbType.UniqueIdentifier).Value = workerId;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetGuid(0));

        return ids;
    }

    public async Task<bool> RenewLeaseAsync(
        Guid requestId,
        Guid workerId,
        TimeSpan lease,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var query = db.WorkerRequests.Where(r =>
            r.Id == requestId && r.State == WorkerRequestState.Claimed);

        // A Guid.Empty owner means "any holder" and is only used by the transport seam.
        if (workerId != Guid.Empty)
            query = query.Where(r => r.ClaimedByWorkerId == workerId);

        var renewed = await query.ExecuteUpdateAsync(setters => setters
            .SetProperty(r => r.LeaseExpiresAt, DateTime.UtcNow.Add(lease))
            .SetProperty(r => r.RowVersion, Guid.NewGuid()), ct);

        return renewed > 0;
    }

    public async Task<WorkerRequest?> CompleteAsync(
        Guid requestId,
        Guid workerId,
        bool succeeded,
        IReadOnlyDictionary<string, string>? outputs,
        string? errorMessage,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var request = await db.WorkerRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request == null) return null;

        // A worker may only report a result for work it currently holds. A request reclaimed by
        // the lease sweep is no longer that worker's to complete.
        if (request.State != WorkerRequestState.Claimed)
        {
            _logger.LogWarning(
                "Worker {WorkerId} tried to complete request {RequestId} in state {State} — ignored",
                workerId, requestId, request.State);
            return request;
        }

        if (workerId != Guid.Empty && request.ClaimedByWorkerId != workerId)
        {
            _logger.LogWarning(
                "Worker {WorkerId} tried to complete request {RequestId} held by {Holder} — ignored",
                workerId, requestId, request.ClaimedByWorkerId);
            return request;
        }

        var now = DateTime.UtcNow;

        request.State = succeeded ? WorkerRequestState.Succeeded : WorkerRequestState.Failed;
        request.CompletedAt = now;
        request.ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage;
        request.LeaseExpiresAt = null;
        request.RowVersion = Guid.NewGuid();

        if (succeeded && outputs is { Count: > 0 })
            request.Outputs = WorkerRequestSerialization.WriteKeyValues(outputs);

        await db.SaveChangesAsync(ct);

        RecordTerminalMetric(request);
        _logger.LogInformation(
            "Worker request {RequestId} {Outcome} after {Attempts} attempt(s): {Error}",
            request.Id, succeeded ? "succeeded" : "failed", request.Attempt, request.ErrorMessage ?? "no error");

        // Releasing the work item is what makes the engine resume. It must happen even though the
        // worker runs in a different process, so it is the completion call that flips the state.
        await ReleaseWorkItemAsync(db, request.WorkItemId, ct);

        return request;
    }

    public async Task<WorkerRequest?> CancelAsync(Guid requestId, string reason, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var request = await db.WorkerRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request == null) return null;
        if (request.IsTerminal) return request;

        request.State = WorkerRequestState.Cancelled;
        request.CompletedAt = DateTime.UtcNow;
        request.ErrorMessage = reason;
        request.LeaseExpiresAt = null;
        request.RowVersion = Guid.NewGuid();

        await db.SaveChangesAsync(ct);
        await ReleaseWorkItemAsync(db, request.WorkItemId, ct);

        _logger.LogWarning("Worker request {RequestId} cancelled: {Reason}", request.Id, reason);
        return request;
    }

    public async Task<WorkerRecoveryResult> RecoverExpiredLeasesAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        // Still has attempts left: requeue so any worker registered under this name can pick it up.
        var requeued = await db.WorkerRequests
            .Where(r => r.State == WorkerRequestState.Claimed
                     && r.LeaseExpiresAt != null
                     && r.LeaseExpiresAt < now
                     && r.Attempt < r.MaxAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, WorkerRequestState.Pending)
                .SetProperty(r => r.ClaimedByWorkerId, (Guid?)null)
                .SetProperty(r => r.ClaimedAt, (DateTime?)null)
                .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(r => r.RowVersion, Guid.NewGuid()), ct);

        if (requeued > 0)
        {
            WorkerMeter.LeasesExpired.Add(requeued);
            _logger.LogWarning("Requeued {Count} worker request(s) after lease expiry", requeued);
        }

        // Attempts exhausted: time out and release the waiting token.
        var exhausted = await db.WorkerRequests
            .Where(r => r.State == WorkerRequestState.Claimed
                     && r.LeaseExpiresAt != null
                     && r.LeaseExpiresAt < now
                     && r.Attempt >= r.MaxAttempts)
            .ToListAsync(ct);

        foreach (var request in exhausted)
        {
            request.State = WorkerRequestState.TimedOut;
            request.CompletedAt = now;
            request.LeaseExpiresAt = null;
            request.ErrorMessage = $"Worker '{request.WorkerName}' lost {request.Attempt} delivery attempt(s) without responding.";
            request.RowVersion = Guid.NewGuid();
        }

        if (exhausted.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            foreach (var request in exhausted)
            {
                WorkerMeter.RequestsTimedOut.Add(1);
                await ReleaseWorkItemAsync(db, request.WorkItemId, ct);
            }

            _logger.LogWarning("Timed out {Count} worker request(s) after exhausting attempts", exhausted.Count);
        }

        return new WorkerRecoveryResult(requeued, exhausted.Count);
    }

    public async Task<int> TimeOutOverdueRequestsAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        // A worker can keep a lease alive forever. The author's timeout is the hard budget: once
        // it is spent the request fails regardless of how recently the worker renewed.
        var overdue = await db.WorkerRequests
            .Where(r => r.State == WorkerRequestState.Claimed
                     && r.CreatedAt < now.AddSeconds(-r.TimeoutSeconds))
            .ToListAsync(ct);

        foreach (var request in overdue)
        {
            request.State = WorkerRequestState.TimedOut;
            request.CompletedAt = now;
            request.LeaseExpiresAt = null;
            request.ErrorMessage = $"Worker '{request.WorkerName}' exceeded the {request.TimeoutSeconds}s budget for this task.";
            request.RowVersion = Guid.NewGuid();
        }

        if (overdue.Count == 0) return 0;

        await db.SaveChangesAsync(ct);

        foreach (var request in overdue)
        {
            WorkerMeter.RequestsTimedOut.Add(1);
            await ReleaseWorkItemAsync(db, request.WorkItemId, ct);
        }

        _logger.LogWarning("Timed out {Count} worker request(s) that exceeded their budget", overdue.Count);
        return overdue.Count;
    }

    public async Task<IReadOnlyList<WorkerRequest>> QueryAsync(WorkerRequestQuery query, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var q = db.WorkerRequests.AsNoTracking();

        if (query.InstanceId is { } instanceId)
            q = q.Where(r => r.InstanceId == instanceId);
        if (!string.IsNullOrWhiteSpace(query.WorkerName))
            q = q.Where(r => r.WorkerName == query.WorkerName);
        if (query.State is { } state)
            q = q.Where(r => r.State == state);

        return await q
            .OrderByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(query.Limit, 1, 500))
            .ToListAsync(ct);
    }

    private void RecordTerminalMetric(WorkerRequest request)
    {
        var durationMs = (DateTime.UtcNow - request.CreatedAt).TotalMilliseconds;
        WorkerMeter.RequestDurationMs.Record(durationMs);

        if (request.State == WorkerRequestState.Succeeded)
            WorkerMeter.RequestsSucceeded.Add(1);
        else
            WorkerMeter.RequestsFailed.Add(1);
    }

    /// <summary>
    /// Moves a waiting work item back to Pending so the engine re-claims the node and the handler
    /// converts the stored request state into a node result. Without this the token waits forever.
    /// </summary>
    private static async Task ReleaseWorkItemAsync(ArgentDbContext db, Guid workItemId, CancellationToken ct)
    {
        if (workItemId == Guid.Empty) return;

        await db.WorkItems
            .Where(w => w.Id == workItemId && w.State == WorkItemState.Waiting)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.State, WorkItemState.Pending)
                .SetProperty(w => w.LockedBy, (string?)null)
                .SetProperty(w => w.LockExpirationUtc, (DateTime?)null), ct);
    }
}
