using Argent.Core.Workers;
using Argent.Core.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Argent.Runtime.Tests.Workers;

public class WorkerRequestQueueTests : WorkerQueueTestBase
{
    public WorkerRequestQueueTests() => BuildQueue();

    [Fact]
    public async Task Enqueue_is_idempotent_per_token_and_node()
    {
        var tokenId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        var first = NewRequest(tokenId: tokenId);
        first.NodeId = nodeId;
        var second = NewRequest(tokenId: tokenId);
        second.NodeId = nodeId;

        var a = await Queue.EnqueueAsync(first);
        var b = await Queue.EnqueueAsync(second);

        // A work item retried after an engine fault re-enters the handler, and it must adopt the
        // request already in flight rather than enqueue a second copy the worker would run twice.
        Assert.Equal(a.Id, b.Id);

        await using var db = CreateContext();
        Assert.Equal(1, await db.WorkerRequests.CountAsync());
    }

    [Fact]
    public async Task Enqueue_persists_parameters()
    {
        var request = NewRequest();
        request.Parameters = WorkerRequestSerialization.WriteKeyValues(new Dictionary<string, string>
        {
            ["template"] = "invoice-{{recordId}}",
            ["retries"] = "2"
        });

        await Queue.EnqueueAsync(request);

        var stored = await Queue.GetAsync(request.Id);
        Assert.NotNull(stored);
        Assert.Equal("invoice-{{recordId}}", stored!.GetParameters()["template"]);
        Assert.Equal("2", stored.GetParameters()["retries"]);
    }

    [Fact]
    public async Task Completing_a_claimed_request_stores_outputs_and_releases_the_waiting_work_item()
    {
        var tokenId = Guid.NewGuid();
        var workItemId = await SeedWaitingWorkItemAsync(tokenId);

        var request = NewRequest(tokenId: tokenId, workItemId: workItemId);
        await Queue.EnqueueAsync(request);

        // Simulate the claim: the queue's claim path is raw SQL, so set the claimed columns directly.
        await using (var db = CreateContext())
        {
            var row = await db.WorkerRequests.FindAsync(request.Id);
            row!.State = WorkerRequestState.Claimed;
            row.ClaimedByWorkerId = Guid.NewGuid();
            row.Attempt = 1;
            row.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(1);
            await db.SaveChangesAsync();
        }

        var workerId = (await Queue.GetAsync(request.Id))!.ClaimedByWorkerId!.Value;

        var completed = await Queue.CompleteAsync(
            request.Id,
            workerId,
            succeeded: true,
            new Dictionary<string, string> { ["pdf"] = "https://files/report.pdf" },
            null);

        Assert.NotNull(completed);
        Assert.Equal(WorkerRequestState.Succeeded, completed!.State);
        Assert.Equal("https://files/report.pdf", completed.GetOutputs()["pdf"]);
        Assert.NotNull(completed.CompletedAt);

        // This transition is what makes the engine resume: without it the token waits forever.
        Assert.Equal(WorkItemState.Pending, await GetWorkItemStateAsync(workItemId));
    }

    [Fact]
    public async Task A_worker_cannot_complete_work_held_by_another_worker()
    {
        var request = NewRequest();
        await Queue.EnqueueAsync(request);

        var holderId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            var row = await db.WorkerRequests.FindAsync(request.Id);
            row!.State = WorkerRequestState.Claimed;
            row.ClaimedByWorkerId = holderId;
            row.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(1);
            await db.SaveChangesAsync();
        }

        var result = await Queue.CompleteAsync(request.Id, Guid.NewGuid(), true, null, null);

        Assert.NotNull(result);
        Assert.Equal(WorkerRequestState.Claimed, result!.State);

        await using var db2 = CreateContext();
        var stored = await db2.WorkerRequests.FindAsync(request.Id);
        Assert.Null(stored!.Outputs);
    }

    [Fact]
    public async Task Renewing_a_lease_extends_it_only_for_the_holder()
    {
        var request = NewRequest();
        await Queue.EnqueueAsync(request);

        var holderId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            var row = await db.WorkerRequests.FindAsync(request.Id);
            row!.State = WorkerRequestState.Claimed;
            row.ClaimedByWorkerId = holderId;
            row.LeaseExpiresAt = DateTime.UtcNow.AddSeconds(1);
            await db.SaveChangesAsync();
        }

        Assert.False(await Queue.RenewLeaseAsync(request.Id, Guid.NewGuid(), TimeSpan.FromMinutes(5), default));
        Assert.True(await Queue.RenewLeaseAsync(request.Id, holderId, TimeSpan.FromMinutes(5), default));

        var renewed = await Queue.GetAsync(request.Id);
        Assert.True(renewed!.LeaseExpiresAt > DateTime.UtcNow.AddMinutes(4));
    }

    [Fact]
    public async Task An_expired_lease_is_requeued_while_attempts_remain()
    {
        var request = NewRequest(maxAttempts: 3);
        await Queue.EnqueueAsync(request);

        await MarkExpiredAsync(request.Id, attempt: 1);

        var result = await Queue.RecoverExpiredLeasesAsync();

        Assert.Equal(1, result.Requeued);
        Assert.Equal(0, result.TimedOut);

        var stored = await Queue.GetAsync(request.Id);
        Assert.Equal(WorkerRequestState.Pending, stored!.State);
        Assert.Null(stored.LeaseExpiresAt);
    }

    [Fact]
    public async Task An_expired_lease_times_out_and_releases_the_token_when_attempts_are_gone()
    {
        var tokenId = Guid.NewGuid();
        var workItemId = await SeedWaitingWorkItemAsync(tokenId);

        var request = NewRequest(tokenId: tokenId, workItemId: workItemId, maxAttempts: 1);
        await Queue.EnqueueAsync(request);
        await MarkExpiredAsync(request.Id, attempt: 1);

        var result = await Queue.RecoverExpiredLeasesAsync();

        Assert.Equal(1, result.TimedOut);
        Assert.Equal(WorkerRequestState.TimedOut, (await Queue.GetAsync(request.Id))!.State);
        Assert.Equal(WorkItemState.Pending, await GetWorkItemStateAsync(workItemId));
    }

    [Fact]
    public async Task A_renewed_lease_still_dies_at_the_authors_budget()
    {
        var tokenId = Guid.NewGuid();
        var workItemId = await SeedWaitingWorkItemAsync(tokenId);

        var request = NewRequest(tokenId: tokenId, workItemId: workItemId, timeoutSeconds: 1);
        request.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
        await Queue.EnqueueAsync(request);

        await using (var db = CreateContext())
        {
            var row = await db.WorkerRequests.FindAsync(request.Id);
            row!.State = WorkerRequestState.Claimed;
            row.ClaimedByWorkerId = Guid.NewGuid();
            // A worker can hold a lease forever; the author's timeout is the hard budget.
            row.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(30);
            await db.SaveChangesAsync();
        }

        var timedOut = await Queue.TimeOutOverdueRequestsAsync();

        Assert.Equal(1, timedOut);
        Assert.Equal(WorkerRequestState.TimedOut, (await Queue.GetAsync(request.Id))!.State);
        Assert.Equal(WorkItemState.Pending, await GetWorkItemStateAsync(workItemId));
    }

    [Fact]
    public async Task Cancelling_releases_the_token_with_the_reason()
    {
        var tokenId = Guid.NewGuid();
        var workItemId = await SeedWaitingWorkItemAsync(tokenId);

        var request = NewRequest(tokenId: tokenId, workItemId: workItemId);
        await Queue.EnqueueAsync(request);

        var cancelled = await Queue.CancelAsync(request.Id, "Cancelled by an administrator.");

        Assert.Equal(WorkerRequestState.Cancelled, cancelled!.State);
        Assert.Equal("Cancelled by an administrator.", cancelled.ErrorMessage);
        Assert.Equal(WorkItemState.Pending, await GetWorkItemStateAsync(workItemId));
    }

    [Fact]
    public async Task Query_filters_by_instance_worker_and_state()
    {
        var instanceId = Guid.NewGuid();
        var mine = NewRequest(workerName: "reports");
        mine.InstanceId = instanceId;
        var other = NewRequest(workerName: "reports");
        var elsewhere = NewRequest(workerName: "mailer");

        await Queue.EnqueueAsync(mine);
        await Queue.EnqueueAsync(other);
        await Queue.EnqueueAsync(elsewhere);
        await MarkExpiredAsync(other.Id, attempt: 1);
        await Queue.CompleteAsync(other.Id, (await Queue.GetAsync(other.Id))!.ClaimedByWorkerId!.Value,
            succeeded: false, outputs: null, errorMessage: "nope");

        Assert.Single(await Queue.QueryAsync(new WorkerRequestQuery(InstanceId: instanceId)));
        Assert.Equal(2, (await Queue.QueryAsync(new WorkerRequestQuery(WorkerName: "reports"))).Count);
        Assert.Equal(2, (await Queue.QueryAsync(new WorkerRequestQuery(State: WorkerRequestState.Pending))).Count);
        Assert.Single(await Queue.QueryAsync(new WorkerRequestQuery(State: WorkerRequestState.Failed)));
    }

    private async Task MarkExpiredAsync(Guid requestId, byte attempt)
    {
        await using var db = CreateContext();
        var row = await db.WorkerRequests.FindAsync(requestId);
        row!.State = WorkerRequestState.Claimed;
        row.ClaimedByWorkerId = Guid.NewGuid();
        row.Attempt = attempt;
        row.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
    }
}
