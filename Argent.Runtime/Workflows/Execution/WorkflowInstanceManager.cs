using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Enums;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Auditing;
using Argent.Models.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Argent.Runtime.Workflows.Execution;

public class WorkflowInstanceManager : IWorkflowInstanceManager
{
    private readonly ArgentDbContext _context;
    private readonly IAuditService _audit;
    private readonly ILogger<WorkflowInstanceManager> _logger;

    public WorkflowInstanceManager(ArgentDbContext context, IAuditService audit, ILogger<WorkflowInstanceManager> logger)
    {
        _context = context;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Guid> StartAsync(
        Guid definitionId,
        IReadOnlyDictionary<string, object?>? variables,
        CancellationToken ct)
    {
        // Load the latest deployed version
        var version = await _context.WorkflowVersions
            .AsNoTracking()
            .Where(v => v.WorkflowId == definitionId
                     && v.State == WorkflowDefinitionState.Deployed)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (version?.Definition == null)
            throw new InvalidOperationException(
                $"No deployed version found for workflow {definitionId}");

        var definition = version.Definition;
        var startNode = definition.Nodes.OfType<StartEvent>().FirstOrDefault();
        if (startNode == null)
            throw new InvalidOperationException(
                $"Workflow {definitionId} has no StartEvent node");

        var instanceId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var payload = variables != null
            ? JsonSerializer.Serialize(variables)
            : "{}";

        // All in a single transaction
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        var instance = new WorkflowInstance
        {
            InstanceId = instanceId,
            WorkflowId = definitionId,
            VersionId = version.Id,
            Name = version.Name,
            Description = version.Description,
            State = InstanceState.Running,
            StartTime = DateTime.UtcNow
        };
        _context.WorkflowInstances.Add(instance);

        var token = new WorkflowToken
        {
            Id = tokenId,
            InstanceId = instanceId,
            NodeId = startNode.Id,
            State = TokenState.Ready,
            Payload = payload,
            CreatedAt = DateTime.UtcNow
        };
        _context.WorkflowTokens.Add(token);

        var workItem = new WorkItem
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            WorkflowInstanceId = instanceId,
            DefinitionId = definitionId,
            NodeId = startNode.Id,
            NodeType = startNode.GetType().Name,
            State = WorkItemState.Pending,
            TokenPayload = payload,
            CreatedAt = DateTime.UtcNow
        };
        _context.WorkItems.Add(workItem);

        await _audit.RecordAsync(
            category: "Workflow",
            eventType: nameof(WorkflowAuditEventType.InstanceStarted),
            instanceId: instanceId,
            tokenId: tokenId,
            details: new { WorkflowName = version.Name, StartNode = startNode.Name },
            ct: ct);

        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Workflow instance {InstanceId} started for '{Name}'",
            instanceId, version.Name);

        return instanceId;
    }

    public async Task SuspendAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await _context.WorkflowInstances.FindAsync([instanceId], ct);
        if (instance == null)
            throw new InvalidOperationException($"Instance {instanceId} not found");

        instance.State = InstanceState.Suspended;
        await _context.SaveChangesAsync(ct);
    }

    public async Task ResumeAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await _context.WorkflowInstances.FindAsync([instanceId], ct);
        if (instance == null)
            throw new InvalidOperationException($"Instance {instanceId} not found");

        instance.State = InstanceState.Running;
        await _context.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(Guid instanceId, CancellationToken ct)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        var instance = await _context.WorkflowInstances.FindAsync([instanceId], ct);
        if (instance == null)
            throw new InvalidOperationException($"Instance {instanceId} not found");

        instance.State = InstanceState.Cancelled;
        instance.EndTime = DateTime.UtcNow;

        // Consume all active tokens
        var activeTokens = await _context.WorkflowTokens
            .Where(t => t.InstanceId == instanceId && t.State != TokenState.Consumed)
            .ToListAsync(ct);

        foreach (var token in activeTokens)
        {
            token.State = TokenState.Consumed;
            token.ConsumedAt = DateTime.UtcNow;
        }

        // Cancel all pending work items
        await _context.WorkItems
            .Where(w => w.WorkflowInstanceId == instanceId
                     && w.State == WorkItemState.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.State, WorkItemState.Failed), ct);

        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<InstanceSnapshot> GetStateAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await _context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.InstanceId == instanceId, ct);

        if (instance == null)
            throw new InvalidOperationException($"Instance {instanceId} not found");

        // Compute the live active-token count rather than storing (and having to maintain)
        // a denormalized counter on the instance.
        var activeTokenCount = await _context.WorkflowTokens
            .CountAsync(t => t.InstanceId == instanceId && t.State != TokenState.Consumed, ct);

        return new InstanceSnapshot(
            instance.InstanceId,
            instance.WorkflowId,
            instance.State,
            activeTokenCount,
            instance.StartTime,
            instance.EndTime);
    }
}
