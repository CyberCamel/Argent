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

        // 2. Carry forward or create token group correlation for gateway join
        var groupId = token.GroupId;
        var tokenCount = token.TokenCount;

        if (groupId == null && request.Targets.Count > 1)
        {
            groupId = Guid.NewGuid();
            tokenCount = request.Targets.Count;
        }

        // 3. Create output tokens and work items for each target
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
                GroupId = groupId,
                TokenCount = tokenCount,
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

        // 4. Record journal entry
        if (request.JournalEntry != null)
        {
            _context.WorkflowJournalEntries.Add(request.JournalEntry);
        }

        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // 5. Post-commit completion check. Running this AFTER the commit means the consumed
        // token is now visible to all concurrent threads. When two EndEvents finish in the same
        // engine batch both run concurrently: checking inside the transaction causes both to see
        // the other's token as still active and both skip completion. Post-commit, at least the
        // later-querying thread will see zero remaining and win the atomic update below.
        if (request.Targets.Count == 0)
        {
            var remaining = await _context.WorkflowTokens
                .CountAsync(t => t.InstanceId == request.InstanceId
                              && t.State != TokenState.Consumed, ct);

            if (remaining == 0)
            {
                // Atomic update: only one concurrent caller can flip Running → Completed.
                var completed = await _context.WorkflowInstances
                    .Where(i => i.InstanceId == request.InstanceId
                             && i.State == InstanceState.Running)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.State, InstanceState.Completed)
                        .SetProperty(i => i.EndTime, DateTime.UtcNow), ct);

                if (completed > 0)
                {
                    _context.WorkflowJournalEntries.Add(new WorkflowJournalEntry
                    {
                        Category = "Workflow",
                        EventType = nameof(WorkflowAuditEventType.InstanceCompleted),
                        InstanceId = request.InstanceId,
                        TokenId = request.ConsumedTokenId,
                        TimeStamp = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync(ct);
                }
            }
        }
    }

    internal static Dictionary<string, object?> DeserializePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return [];
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, object?>>(payload);
            if (raw == null)
                return [];

            // System.Text.Json materializes object? values as JsonElement. Unwrap them
            // to native CLR primitives so condition evaluators (NCalc) and handlers see
            // real numbers/strings/bools rather than JsonElement, which they can't compare.
            var result = new Dictionary<string, object?>(raw.Count);
            foreach (var kvp in raw)
                result[kvp.Key] = UnwrapJsonElement(kvp.Value);
            return result;
        }
        catch { return []; }
    }

    internal static object? UnwrapJsonElement(object? value)
    {
        if (value is not JsonElement element)
            return value;

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
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
