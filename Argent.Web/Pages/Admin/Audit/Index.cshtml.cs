using Argent.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Admin.Audit;

[Authorize(Policy = "FlowAdminOnly")]
public class IndexModel(ArgentDbContext _ctx) : PageModel
{
    public List<AuditEntryRow> Entries { get; set; } = [];
    public string? CategoryFilter { get; set; }
    public string? EventTypeFilter { get; set; }
    public Guid? InstanceFilter { get; set; }

    public async Task<IActionResult> OnGet(
        string? category, string? eventType, Guid? instanceId)
    {
        CategoryFilter = category;
        EventTypeFilter = eventType;
        InstanceFilter = instanceId;

        var query = _ctx.WorkflowJournalEntries
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(e => e.Category == category);

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.EventType == eventType);

        if (instanceId.HasValue)
            query = query.Where(e => e.InstanceId == instanceId.Value);

        var categories = await _ctx.WorkflowJournalEntries
            .AsNoTracking()
            .Select(e => e.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        ViewData["Categories"] = categories;

        var eventTypes = await _ctx.WorkflowJournalEntries
            .AsNoTracking()
            .Select(e => e.EventType)
            .Distinct()
            .OrderBy(et => et)
            .ToListAsync();

        ViewData["EventTypes"] = eventTypes;

        Entries = await query
            .OrderByDescending(e => e.TimeStamp)
            .Select(e => new AuditEntryRow
            {
                Id = e.Id,
                TimeStamp = e.TimeStamp,
                Category = e.Category,
                EventType = e.EventType,
                InstanceId = e.InstanceId,
                TokenId = e.TokenId,
                Actor = e.Actor,
                Details = e.Details
            })
            .ToListAsync();

        return Page();
    }

    public record AuditEntryRow
    {
        public required Guid Id { get; init; }
        public required DateTime TimeStamp { get; init; }
        public required string Category { get; init; }
        public required string EventType { get; init; }
        public required Guid InstanceId { get; init; }
        public Guid? TokenId { get; init; }
        public string? Actor { get; init; }
        public string? Details { get; init; }
    }
}
