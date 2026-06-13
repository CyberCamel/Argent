using Argent.Contracts.Workflows;
using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Enums;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Auditing;
using Argent.Models.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Argent.Runtime.Workflows.Execution;

public class TokenRunner : ITokenRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowNodeRegistry _nodeRegistry;
    private readonly ILogger<TokenRunner> _logger;

    public TokenRunner(
        IServiceScopeFactory scopeFactory,
        IWorkflowNodeRegistry nodeRegistry,
        ILogger<TokenRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _nodeRegistry = nodeRegistry;
        _logger = logger;
    }

    public async Task RunAsync(ClaimedWork claimed, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArgentDbContext>();
        var movement = scope.ServiceProvider.GetRequiredService<ITokenMovement>();

        try
        {
            // Load the latest deployed workflow version
            var version = await db.WorkflowVersions
                .AsNoTracking()
                .Where(v => v.WorkflowId == claimed.DefinitionId
                         && v.State == WorkflowDefinitionState.Deployed)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (version?.Definition == null)
            {
                _logger.LogWarning(
                    "WorkItem {Id}: no deployed definition found for workflow {DefId}",
                    claimed.WorkItemId, claimed.DefinitionId);
                await SetWorkItemStateAsync(db, claimed.WorkItemId, WorkItemState.Failed, ct);
                return;
            }

            var definition = version.Definition;
            var node = definition.Nodes.FirstOrDefault(n => n.Id == claimed.NodeId);

            if (node == null)
            {
                _logger.LogWarning(
                    "WorkItem {Id}: node {NodeId} not found in workflow definition",
                    claimed.WorkItemId, claimed.NodeId);
                await SetWorkItemStateAsync(db, claimed.WorkItemId, WorkItemState.Failed, ct);
                return;
            }

            // Build execution context with current variables
            var variables = TokenMovement.DeserializePayload(claimed.TokenPayload);
            var ctx = new TokenExecutionContext(
                claimed.InstanceId,
                claimed.TokenId,
                claimed.NodeId,
                new TokenVariableBag(variables));

            // Resolve and execute the handler
            var handlers = scope.ServiceProvider.GetRequiredService<IEnumerable<INodeHandler>>();
            var handler = handlers.FirstOrDefault(h => h.HandledNodeType == node.GetType());

            NodeResult result;
            if (handler != null)
            {
                _logger.LogInformation(
                    "Executing {NodeType} '{NodeName}' on instance {InstanceId}",
                    node.GetType().Name, node.Name, claimed.InstanceId);

                result = await handler.ExecuteAsync(node, ctx, ct);
            }
            else
            {
                _logger.LogWarning(
                    "No handler registered for {NodeType}. Completing work item without processing.",
                    node.GetType().Name);
                result = new NodeResult(true);
            }

            // Handle the result
            switch (result.ResultType)
            {
                case NodeResultType.Waiting:
                    await SetWorkItemStateAsync(db, claimed.WorkItemId, WorkItemState.Waiting, ct);
                    _logger.LogInformation(
                        "WorkItem {Id} set to Waiting (node '{NodeName}')",
                        claimed.WorkItemId, node.Name);
                    break;

                case NodeResultType.Failed:
                    await SetWorkItemStateAsync(db, claimed.WorkItemId, WorkItemState.Failed, ct);
                    _logger.LogWarning(
                        "WorkItem {Id} failed: {Error}",
                        claimed.WorkItemId, result.ErrorMessage);
                    break;

                default:
                    // Completed — determine targets and commit
                    var targets = DetermineTargets(node, definition, result, ctx);

                    var journalEntry = new WorkflowJournalEntry
                    {
                        Id = Guid.NewGuid(),
                        InstanceId = claimed.InstanceId,
                        TokenId = claimed.TokenId,
                        EventType = WorkflowAuditEventType.TokenMoved,
                        TimeStamp = DateTime.UtcNow,
                        Details = JsonSerializer.Serialize(new
                        {
                            FromNode = node.Name,
                            FromNodeType = node.GetType().Name,
                            TargetCount = targets.Count,
                            Success = result.Success
                        })
                    };

                    var request = new TokenMovementRequest(
                        claimed.InstanceId,
                        claimed.TokenId,
                        claimed.DefinitionId,
                        targets,
                        journalEntry);

                    await movement.CommitAsync(request, ct);

                    _logger.LogInformation(
                        "Token {TokenId} moved from '{NodeName}' to {TargetCount} target(s)",
                        claimed.TokenId, node.Name, targets.Count);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process WorkItem {Id} for instance {InstanceId}",
                claimed.WorkItemId, claimed.InstanceId);
            await SetWorkItemStateAsync(db, claimed.WorkItemId, WorkItemState.Failed, ct);
        }
    }

    private static List<TokenTarget> DetermineTargets(
        NodeBase node,
        WorkflowDefinition definition,
        NodeResult result,
        ITokenExecutionContext ctx)
    {
        // If the handler explicitly specified target nodes, use those
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

        // Otherwise follow all outbound connections
        var mergedVars = TokenMovement.MergeVariables(
            ctx.Variables.Snapshot(),
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

    private static async Task SetWorkItemStateAsync(
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
}
