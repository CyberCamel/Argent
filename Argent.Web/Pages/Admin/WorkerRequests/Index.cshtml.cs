using Argent.Core.Workers;
using Argent.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Admin.WorkerRequests;

[Authorize(Policy = "FlowAdminOnly")]
public class IndexModel(ArgentDbContext _ctx) : PageModel
{
    public List<RequestListItem> Requests { get; set; } = [];

    public List<string> Workers { get; set; } = [];

    public string? FilterWorkerName { get; set; }

    public WorkerRequestState? FilterState { get; set; }

    public Guid? FilterInstanceId { get; set; }

    public async Task<IActionResult> OnGetAsync(string? workerName, WorkerRequestState? state, Guid? instanceId)
    {
        Workers = await _ctx.Workers.AsNoTracking().OrderBy(w => w.Name).Select(w => w.Name).ToListAsync();

        FilterWorkerName = workerName;
        FilterState = state;
        FilterInstanceId = instanceId;

        var query = _ctx.WorkerRequests.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(workerName))
            query = query.Where(r => r.WorkerName == workerName);
        if (state is { } s)
            query = query.Where(r => r.State == s);
        if (instanceId is { } id)
            query = query.Where(r => r.InstanceId == id);

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(200)
            .ToListAsync();

        // Surface the instance name so a stalled request is identifiable without a second lookup.
        var instanceIds = requests.Select(r => r.InstanceId).Distinct().ToList();
        var names = await _ctx.WorkflowInstances
            .AsNoTracking()
            .Where(i => instanceIds.Contains(i.InstanceId))
            .ToDictionaryAsync(i => i.InstanceId, i => i.Name);

        var now = DateTime.UtcNow;

        Requests = requests.Select(r => new RequestListItem
        {
            Id = r.Id,
            InstanceId = r.InstanceId,
            InstanceName = names.GetValueOrDefault(r.InstanceId),
            WorkerName = r.WorkerName,
            Subject = r.Subject,
            State = r.State,
            Attempt = r.Attempt,
            MaxAttempts = r.MaxAttempts,
            TimeoutSeconds = r.TimeoutSeconds,
            CreatedAt = r.CreatedAt,
            LeaseExpiresAt = r.LeaseExpiresAt,
            ErrorMessage = r.ErrorMessage,
            // Age against the author's budget, so a slow worker is visible before it times out.
            IsOverdue = r.State == WorkerRequestState.Claimed
                        && r.CreatedAt < now.AddSeconds(-r.TimeoutSeconds)
        }).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        var request = await _ctx.WorkerRequests.FindAsync(id);
        if (request == null) return NotFound();

        if (request.IsTerminal)
            return RedirectToPage(new { workerName = FilterWorkerName, state = FilterState });

        request.State = WorkerRequestState.Cancelled;
        request.CompletedAt = DateTime.UtcNow;
        request.ErrorMessage = "Cancelled by an administrator.";
        request.LeaseExpiresAt = null;
        request.RowVersion = Guid.NewGuid();
        await _ctx.SaveChangesAsync();

        // Release the parked work item, otherwise the token waits for a result that will never come.
        await _ctx.WorkItems
            .Where(w => w.Id == request.WorkItemId && w.State == Core.Workflows.Execution.WorkItemState.Waiting)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.State, Core.Workflows.Execution.WorkItemState.Pending)
                .SetProperty(w => w.LockedBy, (string?)null)
                .SetProperty(w => w.LockExpirationUtc, (DateTime?)null));

        return RedirectToPage(new { workerName = FilterWorkerName, state = FilterState });
    }

    public record RequestListItem
    {
        public required Guid Id { get; init; }
        public required Guid InstanceId { get; init; }
        public string? InstanceName { get; init; }
        public required string WorkerName { get; init; }
        public required string Subject { get; init; }
        public required WorkerRequestState State { get; init; }
        public required byte Attempt { get; init; }
        public required byte MaxAttempts { get; init; }
        public required int TimeoutSeconds { get; init; }
        public required DateTime CreatedAt { get; init; }
        public DateTime? LeaseExpiresAt { get; init; }
        public string? ErrorMessage { get; init; }
        public required bool IsOverdue { get; init; }
    }
}
