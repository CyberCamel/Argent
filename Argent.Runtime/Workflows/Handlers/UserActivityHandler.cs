using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
using Argent.Models.Workflows.Auditing;
using Argent.Models.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Argent.Runtime.Workflows.Handlers;

public class UserActivityHandler : INodeHandler
{
    private readonly ArgentDbContext _context;

    public Type HandledNodeType => typeof(UserActivity);

    public UserActivityHandler(ArgentDbContext context)
    {
        _context = context;
    }

    public async Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var activity = (UserActivity)node;

        // Check if this token already has a completed user task
        var existingWorkItem = await _context.WorkItems
            .Where(w => w.TokenId == ctx.TokenId
                     && w.State == WorkItemState.Waiting)
            .FirstOrDefaultAsync(ct);

        if (existingWorkItem == null)
        {
            // First execution — create a journal entry and return Waiting
            var journal = new WorkflowJournalEntry
            {
                Id = Guid.NewGuid(),
                InstanceId = ctx.InstanceId,
                TokenId = ctx.TokenId,
                EventType = WorkflowAuditEventType.TaskCreated,
                TimeStamp = DateTime.UtcNow,
                Details = JsonSerializer.Serialize(new
                {
                    NodeName = activity.Name,
                    UserExperience = activity.UX?.ToString()
                })
            };
            _context.WorkflowJournalEntries.Add(journal);
            await _context.SaveChangesAsync(ct);

            return new NodeResult(true, ResultType: NodeResultType.Waiting);
        }

        // The work item is being re-processed after an external signal
        // For now: proceed past this node
        return new NodeResult(true);
    }
}
