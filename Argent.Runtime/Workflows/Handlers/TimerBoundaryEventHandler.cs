using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Argent.Models.Workflows.BoundaryEvents;
using Argent.Models.Workflows.Execution;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Workflows.Handlers;

public class TimerBoundaryEventHandler(
    IDbContextFactory<ArgentDbContext> dbFactory) : INodeHandler
{
    public Type HandledNodeType => typeof(TimerBoundaryEvent);

    public async Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var boundary = (TimerBoundaryEvent)node;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Find the parent activity token. If it is already consumed (parent completed
        // normally before the timer fired), quietly consume the boundary token with no
        // outgoing flow — the boundary event is moot.
        var parentToken = await db.WorkflowTokens
            .FirstOrDefaultAsync(t => t.InstanceId == ctx.InstanceId
                                   && t.NodeId == boundary.ParentNodeId
                                   && t.State != TokenState.Consumed, ct);

        if (parentToken == null)
            return new NodeResult(true, ExplicitTargetNodeIds: []);

        if (boundary.IsInterrupting)
        {
            // Consume parent token
            parentToken.State = TokenState.Consumed;
            parentToken.ConsumedAt = DateTime.UtcNow;

            // Cancel any open UserTask on the parent token
            await db.UserTasks
                .Where(t => t.TokenId == parentToken.Id
                         && t.State == UserTaskState.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, UserTaskState.Cancelled), ct);

            // Complete the parent's Waiting WorkItem so recovery ignores it
            await db.WorkItems
                .Where(w => w.TokenId == parentToken.Id
                         && w.State == WorkItemState.Waiting)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.State, WorkItemState.Completed), ct);

            await db.SaveChangesAsync(ct);
        }

        return new NodeResult(true);
    }
}
