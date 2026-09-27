using Argent.Core.Workers;
using Argent.Runtime.Workers;
using Argent.Runtime.Tests.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Argent.Runtime.Tests.Workers;

/// <summary>
/// Exercises the worker claim path against a real SQL Server. The claim is raw T-SQL using
/// <c>ROWLOCK, READPAST</c>, which the SQLite suite cannot run, and exclusivity between two
/// workers is the property that actually matters: two processes must never run the same task.
/// </summary>
[Collection("SqlServer")]
[Trait("Category", "Sql")]
public class WorkerClaimSqlServerTests
{
    private readonly SqlServerFixture _fx;

    public WorkerClaimSqlServerTests(SqlServerFixture fx) => _fx = fx;

    private WorkerRequestQueue MakeQueue()
    {
        var factory = new TestDbContextFactory(_fx);
        return new WorkerRequestQueue(
            factory,
            _fx.ConnectionString,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkerRequestQueue>.Instance);
    }

    private async Task<WorkerRequest> SeedAsync(string workerName = "reports", string subject = "render")
    {
        var request = new WorkerRequest
        {
            Id = Guid.NewGuid(),
            InstanceId = Guid.NewGuid(),
            TokenId = Guid.NewGuid(),
            WorkItemId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            WorkerName = workerName,
            Subject = subject,
            Parameters = WorkerRequestSerialization.WriteKeyValues(new Dictionary<string, string> { ["k"] = "v" }),
            State = WorkerRequestState.Pending,
            MaxAttempts = 2,
            CreatedAt = DateTime.UtcNow
        };

        await using var db = _fx.CreateContext();
        db.WorkerRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    [SkippableFact]
    public async Task Claims_a_pending_request_for_the_addressed_worker_only()
    {
        Skip.IfNot(_fx.Available, "Docker / SQL Server not available in this environment");

        var mine = await SeedAsync("reports-a", "render");
        var theirs = await SeedAsync("reports-b", "render");

        var workerId = Guid.NewGuid();
        var claimed = await MakeQueue().ClaimAsync(workerId, "reports-a", 10, TimeSpan.FromMinutes(1));

        var ids = claimed.Requests.Select(r => r.Id).ToList();
        Assert.Contains(mine.Id, ids);
        Assert.DoesNotContain(theirs.Id, ids);

        var claimedRequest = claimed.Requests.Single(r => r.Id == mine.Id);
        Assert.Equal(WorkerRequestState.Claimed, claimedRequest.State);
        Assert.Equal(workerId, claimedRequest.ClaimedByWorkerId);
        Assert.Equal(1, claimedRequest.Attempt);
        Assert.NotNull(claimedRequest.LeaseExpiresAt);
        Assert.Equal("v", claimedRequest.GetParameters()["k"]);
    }

    [SkippableFact]
    public async Task A_second_claim_does_not_hand_out_the_same_request()
    {
        Skip.IfNot(_fx.Available, "Docker / SQL Server not available in this environment");

        var request = await SeedAsync("reports-c", "render");
        var queue = MakeQueue();

        var first = await queue.ClaimAsync(Guid.NewGuid(), "reports-c", 10, TimeSpan.FromMinutes(1));
        Assert.Contains(first.Requests, r => r.Id == request.Id);

        var second = await queue.ClaimAsync(Guid.NewGuid(), "reports-c", 10, TimeSpan.FromMinutes(1));
        Assert.DoesNotContain(second.Requests, r => r.Id == request.Id);
    }

    [SkippableFact]
    public async Task Concurrent_claims_never_overlap_on_the_same_request()
    {
        Skip.IfNot(_fx.Available, "Docker / SQL Server not available in this environment");

        const int total = 12;
        for (var i = 0; i < total; i++)
            await SeedAsync("reports-race", "render");

        var queue = MakeQueue();

        // Several workers racing for the same pool, the way separate processes would. READPAST
        // skips rows another claim holds, so a round can legitimately come back short; what must
        // never happen is the same request being handed to two workers.
        var seen = new List<Guid>();

        for (var round = 0; round < 10; round++)
        {
            var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
                Task.Run(() => queue.ClaimAsync(Guid.NewGuid(), "reports-race", 3, TimeSpan.FromMinutes(1)))));

            var batch = claims.SelectMany(c => c.Requests).ToList();
            if (batch.Count == 0) break;

            seen.AddRange(batch.Select(r => r.Id));
        }

        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(total, seen.Count);

        await using var db = _fx.CreateContext();
        Assert.Equal(total, await db.WorkerRequests.CountAsync(r =>
            r.WorkerName == "reports-race" && r.State == WorkerRequestState.Claimed));
    }

    [SkippableFact]
    public async Task Claims_respect_priority_then_age()
    {
        Skip.IfNot(_fx.Available, "Docker / SQL Server not available in this environment");

        var low = await SeedAsync("reports-order", "render");
        await using (var db = _fx.CreateContext())
        {
            var row = await db.WorkerRequests.FindAsync(low.Id);
            row!.Priority = 1;
            await db.SaveChangesAsync();
        }

        var normal = await SeedAsync("reports-order", "render");

        var claimed = await MakeQueue().ClaimAsync(Guid.NewGuid(), "reports-order", 1, TimeSpan.FromMinutes(1));

        Assert.Equal(low.Id, Assert.Single(claimed.Requests).Id);
        Assert.NotEqual(normal.Id, claimed.Requests[0].Id);
    }

    [SkippableFact]
    public async Task Completing_a_claimed_request_releases_the_waiting_work_item()
    {
        Skip.IfNot(_fx.Available, "Docker / SQL Server not available in this environment");

        var request = await SeedAsync("reports-complete", "render");

        // The request records the work item it must release on completion.
        var workItemId = request.WorkItemId;
        await using (var db = _fx.CreateContext())
        {
            db.WorkItems.Add(new Core.Workflows.Execution.WorkItem
            {
                Id = workItemId,
                TokenId = request.TokenId,
                NodeId = request.NodeId,
                NodeType = "WorkerActivity",
                State = Core.Workflows.Execution.WorkItemState.Waiting,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var queue = MakeQueue();
        var workerId = Guid.NewGuid();
        var claimed = await queue.ClaimAsync(workerId, "reports-complete", 10, TimeSpan.FromMinutes(1));
        Assert.Contains(claimed.Requests, r => r.Id == request.Id);

        await queue.CompleteAsync(request.Id, workerId, true,
            new Dictionary<string, string> { ["reportUrl"] = "https://files/r.pdf" }, null);

        await using var check = _fx.CreateContext();
        var workItem = await check.WorkItems.FindAsync(workItemId);
        Assert.Equal(Core.Workflows.Execution.WorkItemState.Pending, workItem!.State);

        var stored = await check.WorkerRequests.FindAsync(request.Id);
        Assert.Equal(WorkerRequestState.Succeeded, stored!.State);
        Assert.Equal("https://files/r.pdf", stored.GetOutputs()["reportUrl"]);
    }
}

/// <summary>Minimal <see cref="IDbContextFactory{TContext}"/> over the SQL Server fixture.</summary>
internal sealed class TestDbContextFactory(SqlServerFixture fixture) : IDbContextFactory<Argent.Infrastructure.Data.ArgentDbContext>
{
    public Argent.Infrastructure.Data.ArgentDbContext CreateDbContext() => fixture.CreateContext();

    public Task<Argent.Infrastructure.Data.ArgentDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(fixture.CreateContext());
}
