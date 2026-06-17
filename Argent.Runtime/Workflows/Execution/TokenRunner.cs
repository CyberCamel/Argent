using Argent.Contracts.Workflows;
using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.DomainObjects;
using Argent.Models.Enums;
using Argent.Models.Forms;
using Argent.Models.Workflows;
using Argent.Runtime.Workflows;
using Argent.Models.Workflows.Auditing;
using Argent.Models.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;

namespace Argent.Runtime.Workflows.Execution;

public class TokenRunner : ITokenRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowNodeRegistry _nodeRegistry;
    private readonly IDbContextFactory<ArgentDbContext> _contextFactory;
    private readonly ILogger<TokenRunner> _logger;

    public TokenRunner(
        IServiceScopeFactory scopeFactory,
        IWorkflowNodeRegistry nodeRegistry,
        IDbContextFactory<ArgentDbContext> contextFactory,
        ILogger<TokenRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _nodeRegistry = nodeRegistry;
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task RunAsync(ClaimedWork claimed, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArgentDbContext>();
        var movement = scope.ServiceProvider.GetRequiredService<ITokenMovement>();

        try
        {
            // Load the token first — it is the source of truth for InstanceId and Payload,
            // and we check it early to avoid unnecessary work on already-consumed tokens.
            var currentToken = await db.WorkflowTokens.FindAsync([claimed.TokenId], ct);

            if (currentToken == null || currentToken.State == TokenState.Consumed)
            {
                WorkflowMeter.ItemsClaimed.Add(1);
                _logger.LogWarning(
                    "Token {TokenId} already consumed — completing work item {WorkItemId} without processing",
                    claimed.TokenId, claimed.WorkItemId);
                await SetWorkItemStateCoreAsync(db, claimed.WorkItemId, WorkItemState.Completed, ct);
                return;
            }

            var instanceId = currentToken.InstanceId;

            // Resolve the workflow version this instance is pinned to. A running instance must
            // keep executing the definition it started on, even after a newer version is deployed.
            var instance = await db.WorkflowInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InstanceId == instanceId, ct);

            WorkflowVersion? version;
            if (instance != null && instance.VersionId != Guid.Empty)
            {
                version = await db.WorkflowVersions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == instance.VersionId, ct);
            }
            else if (instance != null)
            {
                // Instance exists but has no pinned version (legacy data) — best-effort latest deployed.
                version = await db.WorkflowVersions
                    .AsNoTracking()
                    .Where(v => v.WorkflowId == instance.WorkflowId
                             && v.State == WorkflowDefinitionState.Deployed)
                    .OrderByDescending(v => v.CreatedAt)
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                version = null;
            }

            if (version?.Definition == null)
            {
                _logger.LogWarning(
                    "WorkItem {Id}: no definition resolved for instance {InstanceId} (pinned version {VersionId})",
                    claimed.WorkItemId, instanceId, instance?.VersionId);
                await SetWorkItemStateCoreAsync(db, claimed.WorkItemId, WorkItemState.Failed, ct);
                return;
            }

            var definition = version.Definition;
            var node = definition.Nodes.FirstOrDefault(n => n.Id == claimed.NodeId);

            if (node == null)
            {
                _logger.LogWarning(
                    "WorkItem {Id}: node {NodeId} not found in workflow definition",
                    claimed.WorkItemId, claimed.NodeId);
                await SetWorkItemStateCoreAsync(db, claimed.WorkItemId, WorkItemState.Failed, ct);
                return;
            }

            // --- Gateway JOIN detection ---
            var inboundCount = definition.Connections.Count(c => c.To.Id == node.Id);

            if (inboundCount > 1 && (node is InclusiveGateway or ParallelGateway)
                && currentToken.GroupId != null && currentToken.TokenCount > 0)
            {
                var arrival = await ResolveJoinArrivalAsync(db, claimed, instanceId, node, currentToken, ct);
                if (arrival == JoinArrival.Waiting)
                {
                    await SetWorkItemStateCoreAsync(db, claimed.WorkItemId, WorkItemState.Completed, ct);
                    WorkflowMeter.ItemsClaimed.Add(1);
                    return;
                }

                // JoinArrival.Fire: this token is the final sibling to arrive. It is left in
                // the Ready state and consumed by TokenMovement on the normal fire path below,
                // which produces the join's outgoing token(s).
            }

            // Build execution context
            // Keep engine-set variables (token payload) separate from the enriched set.
            // Record/custom fields are loaded purely for in-memory evaluation — they must
            // NOT be written into the next token's payload, or they show up as "Instance"
            // variables and duplicate what Record/Custom sources already surface.
            var engineVariables = TokenMovement.DeserializePayload(currentToken.Payload);
            var enrichedVariables = await LoadRecordVariablesAsync(db, definition, instance, ct);
            foreach (var kvp in engineVariables)
                enrichedVariables[kvp.Key] = kvp.Value; // engine vars win over record fields

            var candidates = definition.Connections
                .Where(c => c.From.Id == node.Id)
                .Select(c =>
                {
                    var targetNode = definition.Nodes.FirstOrDefault(n => n.Id == c.To.Id);
                    return targetNode != null
                        ? new CandidateTarget(targetNode.Id, targetNode.GetType().Name, ResolveExpression(c), c.Label)
                        : null;
                })
                .Where(c => c != null)
                .Select(c => c!)
                .ToList();

            var ctx = new TokenExecutionContext(
                instanceId,
                claimed.TokenId,
                claimed.NodeId,
                new TokenVariableBag(enrichedVariables),
                candidates,
                currentToken.GroupId,
                currentToken.TokenCount);

            // Resolve handler
            var handlers = scope.ServiceProvider.GetRequiredService<IEnumerable<INodeHandler>>();
            var handler = handlers.FirstOrDefault(h => h.HandledNodeType == node.GetType());

            // Start lock heartbeat during execution
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeatTask = RenewLockPeriodicallyAsync(claimed.WorkItemId, heartbeatCts.Token);

            var sw = Stopwatch.StartNew();
            NodeResult result;
            try
            {
                if (handler != null)
                {
                    _logger.LogInformation(
                        "Executing {NodeType} '{NodeName}' on instance {InstanceId}",
                        node.GetType().Name, node.Name, instanceId);

                    result = await handler.ExecuteAsync(node, ctx, ct);
                }
                else
                {
                    _logger.LogWarning(
                        "No handler registered for {NodeType}. Completing work item without processing.",
                        node.GetType().Name);
                    result = new NodeResult(true);
                }
            }
            finally
            {
                sw.Stop();
                WorkflowMeter.HandlerDurationMs.Record(sw.Elapsed.TotalMilliseconds);
                await heartbeatCts.CancelAsync();
                try { await heartbeatTask; } catch (OperationCanceledException) { }
            }

            // Handle the result
            switch (result.ResultType)
            {
                case NodeResultType.Waiting:
                    WorkflowMeter.ItemsClaimed.Add(1);
                    await SetWorkItemStateCoreAsync(db, claimed.WorkItemId, WorkItemState.Waiting, ct);
                    _logger.LogInformation(
                        "WorkItem {Id} set to Waiting (node '{NodeName}')",
                        claimed.WorkItemId, node.Name);
                    break;

                case NodeResultType.Failed:
                    WorkflowMeter.ItemsClaimed.Add(1);
                    // A handler explicitly returning Failed is a deterministic/business
                    // error (e.g. no matching gateway path) — retrying yields the same
                    // result, so dead-letter it immediately. Transient faults surface as
                    // exceptions and are retried via the catch block below.
                    db.WorkflowJournalEntries.Add(new WorkflowJournalEntry
                    {
                        Category = "Workflow",
                        EventType = nameof(WorkflowAuditEventType.NodeFailed),
                        InstanceId = instanceId,
                        TokenId = claimed.TokenId,
                        TimeStamp = DateTime.UtcNow,
                        Details = JsonSerializer.Serialize(new
                        {
                            Node = node.Name,
                            NodeType = node.GetType().Name,
                            Error = result.ErrorMessage
                        })
                    });
                    await db.SaveChangesAsync(ct);
                    await SetWorkItemStateCoreAsync(db, claimed.WorkItemId, WorkItemState.Failed, ct);
                    _logger.LogWarning(
                        "WorkItem {Id} failed permanently at node '{NodeName}': {Error}",
                        claimed.WorkItemId, node.Name, result.ErrorMessage);
                    break;

                default:
                    // Completed — determine targets and commit
                    var targets = DetermineTargets(node, definition, result, engineVariables);

                    var isGateway = node is ExclusiveGateway or InclusiveGateway or ParallelGateway;
                    var targetNames = targets
                        .Select(t => definition.Nodes.FirstOrDefault(n => n.Id == t.NodeId)?.Name ?? t.NodeType)
                        .ToList();

                    var journalEntry = new WorkflowJournalEntry
                    {
                        Id = Guid.NewGuid(),
                        Category = "Workflow",
                        InstanceId = instanceId,
                        TokenId = claimed.TokenId,
                        EventType = isGateway
                            ? nameof(WorkflowAuditEventType.GatewayEvaluated)
                            : nameof(WorkflowAuditEventType.TokenMoved),
                        Actor = null,
                        TimeStamp = DateTime.UtcNow,
                        Details = JsonSerializer.Serialize(isGateway
                            ? (object)new
                            {
                                Gateway = node.Name,
                                GatewayType = node.GetType().Name,
                                SelectedPaths = targetNames,
                                PathCount = targets.Count
                            }
                            : new
                            {
                                FromNode = node.Name,
                                FromNodeType = node.GetType().Name,
                                Targets = targetNames,
                                TargetCount = targets.Count
                            })
                    };

                    var request = new TokenMovementRequest(
                        instanceId,
                        claimed.TokenId,
                        targets,
                        journalEntry,
                        IsTerminal: node is EndEvent);

                    await movement.CommitAsync(request, ct);
                    await SetWorkItemStateCoreAsync(db, claimed.WorkItemId, WorkItemState.Completed, ct);

                    WorkflowMeter.TokensMoved.Add(targets.Count);
                    WorkflowMeter.ItemsClaimed.Add(1);

                    _logger.LogInformation(
                        "Token {TokenId} moved from '{NodeName}' to {TargetCount} target(s)",
                        claimed.TokenId, node.Name, targets.Count);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process WorkItem {Id}",
                claimed.WorkItemId);
            await HandleFailureAsync(db, claimed, ex.Message, ct);
        }
    }

    private enum JoinArrival
    {
        /// <summary>This token is not the last sibling; it has been consumed and is waiting.</summary>
        Waiting,

        /// <summary>This token is the final sibling; the join should fire.</summary>
        Fire
    }

    /// <summary>
    /// Resolves a token arriving at a merge gateway. Runs under a serializable transaction so
    /// concurrent siblings cannot both observe themselves as "not the last" (which would stall
    /// the join) or both observe themselves as "the last" (which would fire it twice). Exactly
    /// one sibling — the final arrival — returns <see cref="JoinArrival.Fire"/>; the rest are
    /// consumed and return <see cref="JoinArrival.Waiting"/>. Retries on lock contention.
    /// </summary>
    private async Task<JoinArrival> ResolveJoinArrivalAsync(
        ArgentDbContext db,
        ClaimedWork claimed,
        Guid instanceId,
        NodeBase node,
        WorkflowToken currentToken,
        CancellationToken ct)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                // Re-load the token inside the serialized transaction; it may already have been
                // consumed by a recovery pass or a duplicate delivery.
                var token = await db.WorkflowTokens.FirstOrDefaultAsync(t => t.Id == claimed.TokenId, ct);
                if (token == null || token.State == TokenState.Consumed)
                {
                    await tx.CommitAsync(ct);
                    return JoinArrival.Waiting;
                }

                var consumedSiblings = await db.WorkflowTokens
                    .CountAsync(t => t.InstanceId == instanceId
                                  && t.GroupId == currentToken.GroupId
                                  && t.NodeId == node.Id
                                  && t.State == TokenState.Consumed, ct);

                if (consumedSiblings + 1 < currentToken.TokenCount!.Value)
                {
                    token.State = TokenState.Consumed;
                    token.ConsumedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                    _logger.LogInformation(
                        "Token {TokenId} consumed at merge node '{NodeName}' ({Arrived}/{Expected}) — waiting for siblings",
                        claimed.TokenId, node.Name, consumedSiblings + 1, currentToken.TokenCount);
                    return JoinArrival.Waiting;
                }

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "All {Count} sibling tokens arrived at merge node '{NodeName}' — firing gateway",
                    currentToken.TokenCount, node.Name);
                return JoinArrival.Fire;
            }
            catch (Exception ex) when ((ex is DbUpdateException || ex is DbException) && attempt < maxAttempts)
            {
                // Serializable range-lock contention between concurrent siblings (e.g. a deadlock
                // victim). The lost transaction rolled back; retry and observe the winner's commit.
                _logger.LogWarning(ex,
                    "Join arrival contention at merge node '{NodeName}' (attempt {Attempt}/{Max}) — retrying",
                    node.Name, attempt, maxAttempts);
                await Task.Delay(20 * attempt, ct);
            }
        }
    }

    private async Task RenewLockPeriodicallyAsync(Guid workItemId, CancellationToken ct)
    {
        await using var renewDb = await _contextFactory.CreateDbContextAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), ct);
                await renewDb.WorkItems
                    .Where(w => w.Id == workItemId && w.State == WorkItemState.Running)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(w => w.LockExpirationUtc, DateTime.UtcNow.AddMinutes(5)), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lock renewal failed for WorkItem {Id}", workItemId);
            }
        }
    }

    protected internal virtual async Task HandleFailureAsync(
        ArgentDbContext db,
        ClaimedWork claimed,
        string? errorMessage,
        CancellationToken ct)
    {
        if (claimed.RetryCount < claimed.MaxRetries)
        {
            await db.WorkItems
                .Where(w => w.Id == claimed.WorkItemId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(w => w.State, WorkItemState.Pending)
                    .SetProperty(w => w.LockedBy, (string?)null)
                    .SetProperty(w => w.LockExpirationUtc, (DateTime?)null)
                    .SetProperty(w => w.RetryCount, w => w.RetryCount + 1), ct);

            _logger.LogWarning(
                "WorkItem {Id} failed, scheduled retry {RetryCount}/{MaxRetries}: {Error}",
                claimed.WorkItemId, claimed.RetryCount + 1, claimed.MaxRetries, errorMessage);
        }
        else
        {
            await db.WorkItems
                .Where(w => w.Id == claimed.WorkItemId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(w => w.State, WorkItemState.Failed), ct);

            _logger.LogWarning(
                "WorkItem {Id} dead lettered after {MaxRetries} retries: {Error}",
                claimed.WorkItemId, claimed.MaxRetries, errorMessage);
        }
    }

    private static List<TokenTarget> DetermineTargets(
        NodeBase node,
        WorkflowDefinition definition,
        NodeResult result,
        IReadOnlyDictionary<string, object?> engineVariables)
    {
        if (result.ExplicitTargetNodeIds is { Count: > 0 })
        {
            return result.ExplicitTargetNodeIds
                .Select(id => definition.Nodes.FirstOrDefault(n => n.Id == id))
                .Where(n => n != null)
                .Select(n => new TokenTarget(
                    n!.Id,
                    n.GetType().Name,
                    result.OutputVariables))
                .ToList();
        }

        var mergedVars = TokenMovement.MergeVariables(
            engineVariables,
            result.OutputVariables);

        var outbound = definition.Connections
            .Where(c => c.From.Id == node.Id)
            .ToList();

        return outbound
            .Select(c => definition.Nodes.FirstOrDefault(n => n.Id == c.To.Id))
            .Where(n => n != null)
            .Select(n => new TokenTarget(
                n!.Id,
                n.GetType().Name,
                mergedVars))
            .ToList();
    }

    private static string? ResolveExpression(Connection c)
    {
        if (c.IsDefault) return null;
        if (c.Condition != null) return ConditionCompiler.Compile(c.Condition);
        return c.Expression;
    }

    protected internal virtual async Task SetWorkItemStateCoreAsync(
        ArgentDbContext db,
        Guid workItemId,
        WorkItemState state,
        CancellationToken ct)
    {
        await db.WorkItems
            .Where(w => w.Id == workItemId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.State, state), ct);
    }

    private static async Task<Dictionary<string, object?>> LoadRecordVariablesAsync(
        ArgentDbContext db,
        WorkflowDefinition definition,
        WorkflowInstance? instance,
        CancellationToken ct)
    {
        var result = new Dictionary<string, object?>();
        if (instance == null || instance.RecordId == Guid.Empty) return result;

        var startEvent = definition.Nodes.OfType<StartEvent>().FirstOrDefault();
        if (startEvent == null || string.IsNullOrEmpty(startEvent.ObjectKey)) return result;

        var domainObj = await db.DomainObjects
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Key == startEvent.ObjectKey, ct);
        if (domainObj == null) return result;

        var record = await db.DomainObjectRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.DomainObjectId == domainObj.Id && r.Id == instance.RecordId, ct);

        if (record?.Values != null)
            foreach (var kvp in record.Values)
                result[kvp.Key] = TokenMovement.UnwrapJsonElement(kvp.Value);

        var customDataList = await db.FormCustomData
            .AsNoTracking()
            .Where(f => f.RecordId == instance.RecordId)
            .ToListAsync(ct);

        foreach (var customData in customDataList)
            foreach (var kvp in customData.Values)
                result.TryAdd(kvp.Key, TokenMovement.UnwrapJsonElement(kvp.Value));

        return result;
    }
}
