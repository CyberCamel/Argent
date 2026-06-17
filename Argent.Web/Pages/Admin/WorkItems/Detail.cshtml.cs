using Argent.Infrastructure.Data;
using Argent.Models.Workflows.Auditing;
using Argent.Models.Workflows.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Admin.WorkItems;

[Authorize(Policy = "FlowAdminOnly")]
public class DetailModel(ArgentDbContext _ctx) : PageModel
{
    public WorkItemDetail WorkItem { get; set; } = default!;
    public List<AuditRow> AuditEntries { get; set; } = [];

    public async Task<IActionResult> OnGet(Guid id)
    {
        var item = await _ctx.WorkItems
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id);

        if (item == null)
            return NotFound();

        // Resolve instance + node name via the token (InstanceId is no longer on WorkItem).
        string? nodeName = null;
        string? workflowName = null;
        Guid? instanceId = null;

        var token = await _ctx.WorkflowTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == item.TokenId);

        if (token != null)
        {
            instanceId = token.InstanceId;
            var instance = await _ctx.WorkflowInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InstanceId == token.InstanceId);

            if (instance != null)
            {
                workflowName = instance.Name;

                if (instance.VersionId != Guid.Empty)
                {
                    var version = await _ctx.WorkflowVersions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(v => v.Id == instance.VersionId);

                    nodeName = version?.Definition?.Nodes
                        .FirstOrDefault(n => n.Id == item.NodeId)?.Name;
                }
            }
        }

        WorkItem = new WorkItemDetail
        {
            Id                = item.Id,
            InstanceId        = instanceId,
            WorkflowName      = workflowName,
            NodeId            = item.NodeId,
            NodeName          = nodeName,
            NodeType          = item.NodeType,
            TokenId           = item.TokenId,
            State             = item.State,
            Priority          = item.Priority,
            RetryCount        = item.RetryCount,
            MaxRetries        = item.MaxRetries,
            LockedBy          = item.LockedBy,
            LockExpirationUtc = item.LockExpirationUtc,
            CreatedAt         = item.CreatedAt
        };

        AuditEntries = (await _ctx.WorkflowJournalEntries
            .AsNoTracking()
            .Where(e => e.TokenId == item.TokenId)
            .OrderByDescending(e => e.TimeStamp)
            .ToListAsync())
            .Select(e => new AuditRow
            {
                Id        = e.Id,
                TimeStamp = e.TimeStamp,
                Category  = e.Category,
                EventType = e.EventType,
                Actor     = e.Actor,
                Details   = e.Details
            }).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostRetry(Guid id)
    {
        var item = await _ctx.WorkItems.FindAsync(id);
        if (item == null) return NotFound();

        if (item.State != WorkItemState.Failed)
            return RedirectToPage(new { id });

        item.State = WorkItemState.Pending;
        item.LockedBy = null;
        item.LockExpirationUtc = null;
        item.RetryCount = 0;
        await _ctx.SaveChangesAsync();

        return RedirectToPage(new { id });
    }

    public record WorkItemDetail
    {
        public required Guid Id { get; init; }
        public Guid? InstanceId { get; init; }
        public string? WorkflowName { get; init; }
        public required Guid NodeId { get; init; }
        public string? NodeName { get; init; }
        public required string NodeType { get; init; }
        public required Guid TokenId { get; init; }
        public required WorkItemState State { get; init; }
        public required short Priority { get; init; }
        public required byte RetryCount { get; init; }
        public required byte MaxRetries { get; init; }
        public string? LockedBy { get; init; }
        public DateTime? LockExpirationUtc { get; init; }
        public required DateTime CreatedAt { get; init; }
    }

    public record AuditRow
    {
        public required Guid Id { get; init; }
        public required DateTime TimeStamp { get; init; }
        public required string Category { get; init; }
        public required string EventType { get; init; }
        public string? Actor { get; init; }
        public string? Details { get; init; }
    }
}
