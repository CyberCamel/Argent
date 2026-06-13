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

        // 5. Check if instance should complete — only a terminating EndEvent (a terminal
        // node that produces no further targets) can complete an instance, and only once
        // no other active tokens remain. A non-end node with zero targets cannot silently
        // complete the instance; the recovery pass flags it instead.
        if (request.IsTerminal && request.Targets.Count == 0)
        {
            var remaining = await _context.WorkflowTokens
                .CountAsync(t => t.InstanceId == request.InstanceId
                              && t.Id != request.ConsumedTokenId
                              && t.State != TokenState.Consumed, ct);

            if (remaining == 0)
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

    private static object? UnwrapJsonElement(object? value)
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
