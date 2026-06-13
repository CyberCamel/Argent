using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Argent.Runtime.Workflows.Execution;

public class RecoveryPass
{
    private readonly IDbContextFactory<ArgentDbContext> _contextFactory;
    private readonly ILogger<RecoveryPass> _logger;

    public RecoveryPass(
        IDbContextFactory<ArgentDbContext> contextFactory,
        ILogger<RecoveryPass> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Recovery pass started");

        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;

        // Step 1: Release stale locks
        var releasedCount = await context.WorkItems
            .Where(w => w.State == WorkItemState.Running
                     && w.LockExpirationUtc != null
                     && w.LockExpirationUtc < now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.State, WorkItemState.Pending)
                .SetProperty(w => w.LockedBy, (string?)null)
                .SetProperty(w => w.LockExpirationUtc, (DateTime?)null)
                .SetProperty(w => w.RetryCount, w => w.RetryCount + 1), ct);

        if (releasedCount > 0)
            _logger.LogWarning("Released {Count} stale work item locks", releasedCount);

        // Step 2: Recover orphan tokens — tokens in Ready state with no corresponding
        // Pending or Running work item
        var readyTokens = await context.WorkflowTokens
            .Where(t => t.State == TokenState.Ready)
            .ToListAsync(ct);

        var recoveredCount = 0;
        foreach (var token in readyTokens)
        {
            var hasWorkItem = await context.WorkItems
                .AnyAsync(w => w.TokenId == token.Id
                            && (w.State == WorkItemState.Pending
                             || w.State == WorkItemState.Running), ct);

            if (hasWorkItem)
                continue;

            // Find the definition ID from the instance
            var instance = await context.WorkflowInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InstanceId == token.InstanceId, ct);

            if (instance == null)
            {
                // Orphan token with no instance — consume it
                token.State = TokenState.Consumed;
                token.ConsumedAt = DateTime.UtcNow;
                _logger.LogWarning(
                    "Consumed orphan token {TokenId} (no instance found)", token.Id);
                continue;
            }

            // Create a new work item for this token
            context.WorkItems.Add(new WorkItem
            {
                Id = Guid.NewGuid(),
                TokenId = token.Id,
                WorkflowInstanceId = token.InstanceId,
                DefinitionId = instance.WorkflowId,
                NodeId = token.NodeId,
                NodeType = "Unknown",
                State = WorkItemState.Pending,
                TokenPayload = token.Payload,
                CreatedAt = DateTime.UtcNow
            });
            recoveredCount++;
        }

        if (recoveredCount > 0)
        {
            _logger.LogWarning(
                "Recovered {Count} orphan tokens — created work items", recoveredCount);
        }

        // Step 3: Detect stuck instances
        var runningInstances = await context.WorkflowInstances
            .Where(i => i.State == InstanceState.Running)
            .ToListAsync(ct);

        var stuckCount = 0;
        foreach (var instance in runningInstances)
        {
            var activeTokenCount = await context.WorkflowTokens
                .CountAsync(t => t.InstanceId == instance.InstanceId
                              && t.State != TokenState.Consumed, ct);

            if (activeTokenCount == 0)
            {
                instance.State = InstanceState.Completed;
                instance.EndTime = DateTime.UtcNow;
                stuckCount++;
                _logger.LogWarning(
                    "Instance {InstanceId} had zero active tokens — marking Completed",
                    instance.InstanceId);
            }
        }

        if (stuckCount > 0)
            _logger.LogWarning("Recovered {Count} stuck instances", stuckCount);

        await context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recovery pass complete: {Released} locks released, {Recovered} tokens recovered, {Stuck} instances recovered",
            releasedCount, recoveredCount, stuckCount);
    }
}
