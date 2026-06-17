using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Intermediates;
using Argent.Models.Workflows.Shared;
using Argent.Runtime.Workflows.Execution;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Workflows.Handlers;

public class CatchingTimerHandler(
    IDbContextFactory<ArgentDbContext> dbFactory,
    TimerManager timerManager) : INodeHandler
{
    public Type HandledNodeType => typeof(CatchingTimerEvent);

    public async Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var timerNode = (CatchingTimerEvent)node;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var existing = await db.Timers
            .FirstOrDefaultAsync(t => t.TokenId == ctx.TokenId, ct);

        if (existing == null)
        {
            var anchor = ResolveAnchor(timerNode.Definition, ctx);
            var triggerTime = TimerDefinitionResolver.Resolve(timerNode.Definition, anchor);
            await timerManager.CreateAsync(ctx.TokenId, ctx.NodeId, "timer-catch", triggerTime, ct);
            return new NodeResult(true, ResultType: NodeResultType.Waiting);
        }

        return existing.State switch
        {
            TimerState.Fired => new NodeResult(true),
            _                => new NodeResult(true, ResultType: NodeResultType.Waiting)
        };
    }

    // Uses the domain object field date as anchor if configured; falls back to now.
    private static DateTime ResolveAnchor(TimerDefinition definition, ITokenExecutionContext ctx)
    {
        if (definition is RelativeTimerDefinition { UseField: true, FieldKey: { } key })
        {
            var fieldVal = ctx.Variables.Get<DateTime?>(key);
            if (fieldVal.HasValue)
                return fieldVal.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(fieldVal.Value, DateTimeKind.Utc)
                    : fieldVal.Value.ToUniversalTime();
        }
        return DateTime.UtcNow;
    }
}
