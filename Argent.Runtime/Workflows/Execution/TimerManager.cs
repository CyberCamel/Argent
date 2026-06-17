using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DbTimer = Argent.Models.Workflows.Shared.Timer;

namespace Argent.Runtime.Workflows.Execution;

public class TimerManager(
    IDbContextFactory<ArgentDbContext> dbFactory,
    ILogger<TimerManager> logger)
{
    private static readonly TimeSpan LookaheadWindow = TimeSpan.FromMinutes(1);

    // Called by TimerCatchHandler on first visit to a timer node.
    public async Task CreateAsync(Guid tokenId, Guid nodeId, string nodeType, DateTime triggerTime, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Timers.Add(new DbTimer
        {
            TokenId = tokenId,
            NodeId = nodeId,
            NodeType = nodeType,
            TriggerTime = triggerTime,
            State = TimerState.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    // Called by the engine each poll cycle. Finds Pending timers due within the lookahead
    // window, atomically claims each one, and schedules an in-memory Task.Delay. Past-due
    // timers are covered by the same path — their delay is clamped to zero.
    public async Task SchedulePendingAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.Add(LookaheadWindow);

        var candidates = await db.Timers
            .Where(t => t.State == TimerState.Pending && t.TriggerTime <= cutoff)
            .ToListAsync(ct);

        foreach (var timer in candidates)
        {
            // Atomic claim — prevents a second engine instance from double-scheduling.
            var claimed = await db.Timers
                .Where(t => t.Id == timer.Id && t.State == TimerState.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, TimerState.Enqueued), ct);

            if (claimed == 0) continue;

            var delay = timer.TriggerTime - DateTime.UtcNow;
            var capture = timer; // capture for closure

            _ = Task.Run(async () =>
            {
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);

                await FireAsync(capture, ct);
            }, ct);
        }
    }

    // Resets any Enqueued timers back to Pending so SchedulePendingAsync re-claims them.
    // Call on engine startup — in-memory Task.Delays are lost across restarts.
    public async Task ResetEnqueuedAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reset = await db.Timers
            .Where(t => t.State == TimerState.Enqueued)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, TimerState.Pending), ct);

        if (reset > 0)
            logger.LogInformation("Reset {Count} Enqueued timers to Pending after restart", reset);
    }

    private async Task FireAsync(DbTimer timer, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            // Verify the timer is still ours — it may have been cancelled while we waited.
            var fired = await db.Timers
                .Where(t => t.Id == timer.Id && t.State == TimerState.Enqueued)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, TimerState.Fired), ct);

            if (fired == 0)
            {
                logger.LogDebug("Timer {TimerId} was cancelled or already fired — skipping", timer.Id);
                return;
            }

            // Catching timers already have a Waiting work item — re-pend it so the handler
            // re-runs and sees the Fired state.  Boundary timers have no work item yet
            // (their token is Waiting with no associated WI) — create one for them.
            var repended = await db.WorkItems
                .Where(wi => wi.TokenId == timer.TokenId && wi.State == WorkItemState.Waiting)
                .ExecuteUpdateAsync(s => s.SetProperty(wi => wi.State, WorkItemState.Pending), ct);

            if (repended == 0)
            {
                db.WorkItems.Add(new WorkItem
                {
                    Id = Guid.NewGuid(),
                    TokenId = timer.TokenId,
                    NodeId = timer.NodeId,
                    NodeType = timer.NodeType,
                    State = WorkItemState.Pending,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync(ct);
            }

            logger.LogInformation("Timer {TimerId} fired — WorkItem {Action} for token {TokenId}",
                timer.Id, repended > 0 ? "re-pended" : "created", timer.TokenId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to fire timer {TimerId}", timer.Id);
        }
    }
}
