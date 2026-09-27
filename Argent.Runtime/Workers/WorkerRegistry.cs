using System.Security.Cryptography;
using System.Text;
using Argent.Core.Workers;
using Argent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Argent.Runtime.Workers;

/// <summary>Registry of worker identities, backed by the Workers table.</summary>
public class WorkerRegistry : IWorkerRegistry
{
    private readonly IDbContextFactory<ArgentDbContext> _contextFactory;
    private readonly ILogger<WorkerRegistry> _logger;

    public WorkerRegistry(IDbContextFactory<ArgentDbContext> contextFactory, ILogger<WorkerRegistry> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<Worker?> GetByIdAsync(Guid workerId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Workers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workerId, ct);
    }

    public async Task<Worker?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Workers.AsNoTracking().FirstOrDefaultAsync(w => w.Name == name, ct);
    }

    public async Task<IReadOnlyList<Worker>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Workers.AsNoTracking().OrderBy(w => w.Name).ToListAsync(ct);
    }

    public async Task<Worker?> FindByApiKeyHashAsync(string apiKeyHash, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Workers
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.ApiKeyHash == apiKeyHash, ct);
    }

    public async Task<bool> IsRegisteredAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Workers
            .AsNoTracking()
            .AnyAsync(w => w.Name == name && w.Status != WorkerStatus.Disabled, ct);
    }

    public async Task<bool> NameIsAvailableAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return !await db.Workers.AsNoTracking().AnyAsync(w => w.Name == name, ct);
    }

    public async Task<WorkerProvisioningResult> ProvisionAsync(Worker worker, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        if (await db.Workers.AnyAsync(w => w.Name == worker.Name, ct))
        {
            // Provisioning is an administrator action, so a taken name is reported rather than
            // taken over the way the old self-registration endpoint did.
            throw new InvalidOperationException($"A worker named '{worker.Name}' is already registered.");
        }

        var (plaintext, hash) = CreateApiKey();
        worker.ApiKeyHash = hash;
        worker.Status = WorkerStatus.Offline;
        worker.LastHeartbeatAt = null;
        worker.InFlight = 0;
        worker.CurrentSubject = null;
        worker.KeyRotations = 0;
        worker.RegisteredAt = DateTime.UtcNow;
        worker.RowVersion = Guid.NewGuid();

        db.Workers.Add(worker);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Provisioned worker '{Name}' (id {WorkerId}); awaiting first heartbeat",
            worker.Name, worker.Id);

        return new WorkerProvisioningResult(worker, plaintext);
    }

    public async Task<WorkerProvisioningResult> RotateApiKeyAsync(Guid workerId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var worker = await db.Workers.FirstOrDefaultAsync(w => w.Id == workerId, ct)
            ?? throw new InvalidOperationException($"Worker {workerId} not found.");

        var (plaintext, hash) = CreateApiKey();

        worker.ApiKeyHash = hash;
        worker.KeyRotations++;
        worker.RowVersion = Guid.NewGuid();

        await db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Rotated the API key for worker '{Name}'; the previous key no longer works", worker.Name);

        return new WorkerProvisioningResult(worker, plaintext);
    }

    public async Task<bool> HeartbeatAsync(
        Guid workerId,
        string? runtime,
        IReadOnlyList<string>? subjects,
        int inFlight,
        string? currentSubject,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var worker = await db.Workers.FirstOrDefaultAsync(w => w.Id == workerId, ct);
        if (worker == null || worker.Status == WorkerStatus.Disabled) return false;

        worker.LastHeartbeatAt = DateTime.UtcNow;
        worker.InFlight = Math.Max(0, inFlight);
        worker.CurrentSubject = string.IsNullOrWhiteSpace(currentSubject) ? null : currentSubject;
        worker.Status = inFlight > 0 ? WorkerStatus.Busy : WorkerStatus.Idle;
        worker.RowVersion = Guid.NewGuid();

        // The client describes the running process, so its report wins. Omitting a field leaves the
        // stored value alone rather than blanking it, so a minimal client does not erase detail.
        if (!string.IsNullOrWhiteSpace(runtime))
            worker.Runtime = runtime.Trim();

        if (subjects is not null)
            worker.SetSubjects(subjects);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SetStatusAsync(Guid workerId, WorkerStatus status, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var worker = await db.Workers.FirstOrDefaultAsync(w => w.Id == workerId, ct);
        if (worker == null) return;

        worker.Status = status;
        worker.InFlight = 0;
        worker.CurrentSubject = null;
        worker.RowVersion = Guid.NewGuid();

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Worker '{Name}' is now {Status}", worker.Name,
            status == WorkerStatus.Disabled ? "revoked" : status.ToString().ToLowerInvariant());
    }

    public async Task<bool> DeleteAsync(Guid workerId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var worker = await db.Workers.FirstOrDefaultAsync(w => w.Id == workerId, ct);
        if (worker == null) return false;

        // The requests stay. They record what a running instance was told, and are reachable from
        // the instance and admin views regardless of whether the worker still exists.
        db.Workers.Remove(worker);
        await db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Deleted worker registration '{Name}'; its request history is retained", worker.Name);

        return true;
    }

    public async Task<int> MarkStaleWorkersOfflineAsync(TimeSpan staleAfter, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow - staleAfter;

        // Only workers that are not already settled, so the sweep does not keep rewriting rows and
        // does not flip a deliberately revoked worker back on.
        var stale = await db.Workers
            .Where(w => w.Status != WorkerStatus.Offline
                     && w.Status != WorkerStatus.Disabled
                     && (w.LastHeartbeatAt == null || w.LastHeartbeatAt < cutoff))
            .ToListAsync(ct);

        if (stale.Count == 0) return 0;

        foreach (var worker in stale)
        {
            worker.Status = WorkerStatus.Offline;
            worker.InFlight = 0;
            worker.CurrentSubject = null;
            worker.RowVersion = Guid.NewGuid();
        }

        await db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Workers marked offline after missing heartbeats: {Workers}",
            string.Join(", ", stale.Select(w => w.Name)));

        return stale.Count;
    }

    /// <summary>Generates a bearer key and returns the plaintext to show once, plus the hash to store.</summary>
    public static (string Plaintext, string Hash) CreateApiKey()
    {
        var plaintext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (plaintext, HashApiKey(plaintext));
    }

    public static string HashApiKey(string plaintext)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
}
