using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Auditing;
using Argent.Models.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Argent.Runtime.Workflows.Execution;

public class TokenMovement : ITokenMovement
{
    private readonly ArgentDbContext _context;

    public TokenMovement(ArgentDbContext context)
    {
        _context = context;
    }

    public async Task CommitAsync(TokenMovementRequest request, CancellationToken ct)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        // 1. Consume the input token
        var token = await _context.WorkflowTokens.FindAsync(
            [request.ConsumedTokenId], ct);

        if (token == null)
            throw new InvalidOperationException(
                $"Token {request.ConsumedTokenId} not found");

        token.State = TokenState.Consumed;
        token.ConsumedAt = DateTime.UtcNow;

        // 2. Create output tokens and work items for each target
        foreach (var target in request.Targets)
        {
            var newToken = new WorkflowToken
            {
                Id = Guid.NewGuid(),
                InstanceId = request.InstanceId,
                NodeId = target.NodeId,
                State = TokenState.Ready,
                Payload = target.Variables != null
                    ? JsonSerializer.Serialize(target.Variables)
                    : token.Payload,
                CreatedAt = DateTime.UtcNow
            };
            _context.WorkflowTokens.Add(newToken);

            var workItem = new WorkItem
            {
                Id = Guid.NewGuid(),
                TokenId = newToken.Id,
                WorkflowInstanceId = request.InstanceId,
                DefinitionId = request.DefinitionId,
                NodeId = target.NodeId,
                NodeType = target.NodeType,
                State = WorkItemState.Pending,
                TokenPayload = newToken.Payload,
                CreatedAt = DateTime.UtcNow
            };
            _context.WorkItems.Add(workItem);
        }

        // 3. Record journal entry
        if (request.JournalEntry != null)
        {
            _context.WorkflowJournalEntries.Add(request.JournalEntry);
        }

        // 4. Check if instance should complete
        if (request.Targets.Count == 0)
        {
            var activeTokenCount = await _context.WorkflowTokens
                .CountAsync(t => t.InstanceId == request.InstanceId
                              && t.State != TokenState.Consumed, ct);

            if (activeTokenCount == 0)
            {
                var instance = await _context.WorkflowInstances
                    .FindAsync([request.InstanceId], ct);

                if (instance != null)
                {
                    instance.State = InstanceState.Completed;
                    instance.EndTime = DateTime.UtcNow;
                }
            }
        }

        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    internal static Dictionary<string, object?> DeserializePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, object?>>(payload) ?? []; }
        catch { return []; }
    }

    internal static string SerializePayload(IReadOnlyDictionary<string, object?>? variables)
    {
        if (variables == null || variables.Count == 0)
            return "{}";
        return JsonSerializer.Serialize(variables);
    }

    internal static Dictionary<string, object?> MergeVariables(
        IReadOnlyDictionary<string, object?> current,
        IReadOnlyDictionary<string, object?>? delta)
    {
        if (delta == null || delta.Count == 0)
            return new Dictionary<string, object?>(current);

        var merged = new Dictionary<string, object?>(current);
        foreach (var kvp in delta)
        {
            merged[kvp.Key] = kvp.Value;
        }
        return merged;
    }
}
