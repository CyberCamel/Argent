using Argent.Infrastructure.Data;
using Argent.Core.Identity;
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

    private const int PageSize = 25;
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }

    public async Task<IActionResult> OnGet(
        string? category, string? eventType, Guid? instanceId, int? page)
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

        CurrentPage = Math.Max(1, page ?? 1);
        var total = await query.CountAsync();
        TotalCount = total;
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        CurrentPage = Math.Min(CurrentPage, TotalPages);

        var rows = await query
            .OrderByDescending(e => e.TimeStamp)
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        // Resolve actors that were logged as an internal user GUID or a known sentinel.
        var users = await _ctx.Users.AsNoTracking()
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
            .ToListAsync();

        var byId = users.ToDictionary(
            u => u.Id.ToString(),
            u => (u.FirstName, u.LastName, u.UserName),
            StringComparer.OrdinalIgnoreCase);

        var byUserName = users.ToDictionary(
            u => u.UserName ?? string.Empty,
            u => (u.FirstName, u.LastName, u.UserName),
            StringComparer.OrdinalIgnoreCase);

        Entries = rows.Select(e => new AuditEntryRow
        {
            Id = e.Id,
            TimeStamp = e.TimeStamp,
            Category = e.Category,
            EventType = e.EventType,
            InstanceId = e.InstanceId,
            TokenId = e.TokenId,
            Actor = ResolveActor(e.Actor, byId, byUserName),
            Details = e.Details
        }).ToList();

        return Page();
    }

    private static string? ResolveActor(
        string? actor,
        IReadOnlyDictionary<string, (string FirstName, string LastName, string? UserName)> byId,
        IReadOnlyDictionary<string, (string FirstName, string LastName, string? UserName)> byUserName)
    {
        if (string.IsNullOrWhiteSpace(actor)) return null;
        if (actor is "Unknown" or "system") return "System";

        if (Guid.TryParse(actor, out var userId)
            && byId.TryGetValue(userId.ToString(), out var byGuid))
            return DisplayName(byGuid);

        if (byUserName.TryGetValue(actor, out var byName))
            return DisplayName(byName);

        return actor;
    }

    private static string DisplayName((string FirstName, string LastName, string? UserName) u) =>
        $"{u.FirstName} {u.LastName} ({u.UserName})";

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
