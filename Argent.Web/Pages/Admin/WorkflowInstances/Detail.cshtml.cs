using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Auditing;
using Argent.Models.Workflows.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Admin.WorkflowInstances;

[Authorize(Policy = "FlowAdminOnly")]
public class DetailModel(
    ArgentDbContext _ctx,
    IWorkflowInstanceManager _instanceManager) : PageModel
{
    public InstanceDetail Instance { get; set; } = default!;
    public List<TokenRow> Tokens { get; set; } = [];
    public List<WorkItemRow> WorkItems { get; set; } = [];
    public List<UserTaskRow> UserTasks { get; set; } = [];
    public List<AuditRow> AuditEntries { get; set; } = [];

    public async Task<IActionResult> OnGet(Guid id)
    {
        var instance = await _ctx.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.InstanceId == id);

        if (instance == null)
            return NotFound();

        var snapshot = await _instanceManager.GetStateAsync(id, default);

        Instance = new InstanceDetail
        {
            InstanceId = instance.InstanceId,
            WorkflowId = instance.WorkflowId,
            WorkflowName = instance.Name,
            Description = instance.Description,
            State = instance.State,
            StartTime = instance.StartTime,
            EndTime = instance.EndTime,
            RecordId = instance.RecordId,
            ActiveTokenCount = snapshot.CurrentTokenCount
        };

        var tokens = await _ctx.WorkflowTokens
            .AsNoTracking()
            .Where(t => t.InstanceId == id)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

        Tokens = tokens.Select(t => new TokenRow
        {
            Id = t.Id,
            NodeId = t.NodeId,
            State = t.State,
            Payload = t.Payload,
            GroupId = t.GroupId,
            TokenCount = t.TokenCount,
            CreatedAt = t.CreatedAt,
            ConsumedAt = t.ConsumedAt
        }).ToList();

        var workItems = await _ctx.WorkItems
            .AsNoTracking()
            .Where(w => w.WorkflowInstanceId == id)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync();

        WorkItems = workItems.Select(w => new WorkItemRow
        {
            Id = w.Id,
            NodeId = w.NodeId,
            NodeType = w.NodeType,
            State = w.State,
            Priority = w.Priority,
            RetryCount = w.RetryCount,
            MaxRetries = w.MaxRetries,
            LockedBy = w.LockedBy,
            LockExpirationUtc = w.LockExpirationUtc,
            CreatedAt = w.CreatedAt
        }).ToList();

        var userTasks = await _ctx.UserTasks
            .AsNoTracking()
            .Where(t => t.InstanceId == id)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

        UserTasks = userTasks.Select(t => new UserTaskRow
        {
            Id = t.Id,
            TokenId = t.TokenId,
            NodeId = t.NodeId,
            State = t.State,
            Title = t.Title,
            Description = t.Description,
            AssignedTo = t.AssignedTo,
            Priority = t.Priority,
            FormId = t.FormId,
            CreatedAt = t.CreatedAt,
            CompletedAt = t.CompletedAt,
            CompletedBy = t.CompletedBy,
            DueDate = t.DueDate
        }).ToList();

        var auditEntries = await _ctx.WorkflowJournalEntries
            .AsNoTracking()
            .Where(e => e.InstanceId == id)
            .OrderByDescending(e => e.TimeStamp)
            .ToListAsync();

        AuditEntries = auditEntries.Select(e => new AuditRow
        {
            Id = e.Id,
            TimeStamp = e.TimeStamp,
            Category = e.Category,
            EventType = e.EventType,
            TokenId = e.TokenId,
            Actor = e.Actor,
            Details = e.Details
        }).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostSuspend(Guid id)
    {
        await _instanceManager.SuspendAsync(id, default);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostResume(Guid id)
    {
        await _instanceManager.ResumeAsync(id, default);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCancel(Guid id)
    {
        await _instanceManager.CancelAsync(id, default);
        return RedirectToPage(new { id });
    }

    public record InstanceDetail
    {
        public required Guid InstanceId { get; init; }
        public required Guid WorkflowId { get; init; }
        public required string WorkflowName { get; init; }
        public string Description { get; init; } = "";
        public required InstanceState State { get; init; }
        public required DateTime StartTime { get; init; }
        public DateTime? EndTime { get; init; }
        public Guid RecordId { get; init; }
        public required int ActiveTokenCount { get; init; }
    }

    public record TokenRow
    {
        public required Guid Id { get; init; }
        public required Guid NodeId { get; init; }
        public required TokenState State { get; init; }
        public string? Payload { get; init; }
        public Guid? GroupId { get; init; }
        public int? TokenCount { get; init; }
        public required DateTime CreatedAt { get; init; }
        public DateTime? ConsumedAt { get; init; }
    }

    public record WorkItemRow
    {
        public required Guid Id { get; init; }
        public required Guid NodeId { get; init; }
        public string NodeType { get; init; } = "";
        public required WorkItemState State { get; init; }
        public short Priority { get; init; }
        public byte RetryCount { get; init; }
        public byte MaxRetries { get; init; }
        public string? LockedBy { get; init; }
        public DateTime? LockExpirationUtc { get; init; }
        public required DateTime CreatedAt { get; init; }
    }

    public record UserTaskRow
    {
        public required Guid Id { get; init; }
        public required Guid TokenId { get; init; }
        public required Guid NodeId { get; init; }
        public required UserTaskState State { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
        public string? AssignedTo { get; init; }
        public short Priority { get; init; }
        public Guid? FormId { get; init; }
        public required DateTime CreatedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public string? CompletedBy { get; init; }
        public DateTime? DueDate { get; init; }
    }

    public record AuditRow
    {
        public required Guid Id { get; init; }
        public required DateTime TimeStamp { get; init; }
        public required string Category { get; init; }
        public required string EventType { get; init; }
        public Guid? TokenId { get; init; }
        public string? Actor { get; init; }
        public string? Details { get; init; }
    }
}
