using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.DomainObjects;
using Argent.Models.Forms;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Auditing;
using Argent.Models.Workflows.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
    public List<VariableRow> Variables { get; set; } = [];

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
            .Where(w => _ctx.WorkflowTokens.Any(t => t.Id == w.TokenId && t.InstanceId == id))
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

        Variables = await LoadVariablesAsync(instance, tokens);

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

    public record VariableRow(string Source, string Key, string DisplayValue);

    private async Task<List<VariableRow>> LoadVariablesAsync(
        WorkflowInstance instance,
        List<WorkflowToken> tokens)
    {
        var rows = new List<VariableRow>();
        var externalKeys = new HashSet<string>(); // record + custom keys, excluded from Instance

        // --- Record fields and custom data ---
        if (instance.RecordId != Guid.Empty)
        {
            var version = instance.VersionId != Guid.Empty
                ? await _ctx.WorkflowVersions.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == instance.VersionId)
                : null;

            var objectKey = version?.Definition?.Nodes
                .OfType<StartEvent>()
                .FirstOrDefault()?.ObjectKey;

            if (!string.IsNullOrEmpty(objectKey))
            {
                var domainObj = await _ctx.DomainObjects.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Key == objectKey);

                if (domainObj != null)
                {
                    var record = await _ctx.DomainObjectRecords.AsNoTracking()
                        .FirstOrDefaultAsync(r => r.DomainObjectId == domainObj.Id
                                               && r.Id == instance.RecordId);

                    if (record?.Values != null)
                        foreach (var kvp in record.Values)
                        {
                            externalKeys.Add(kvp.Key);
                            rows.Add(new VariableRow("Record", kvp.Key, FormatValue(UnwrapValue(kvp.Value))));
                        }
                }
            }

            var customData = await _ctx.FormCustomData.AsNoTracking()
                .Where(f => f.RecordId == instance.RecordId)
                .ToListAsync();

            foreach (var cd in customData)
                foreach (var kvp in cd.Values)
                {
                    externalKeys.Add(kvp.Key);
                    // Custom data can overlap with record fields (same key, form-specific override)
                    rows.RemoveAll(r => r.Source == "Record" && r.Key == kvp.Key);
                    rows.Add(new VariableRow("Custom", kvp.Key, FormatValue(UnwrapValue(kvp.Value))));
                }
        }

        // --- Instance variables from token payloads ---
        // Exclude keys already covered by Record/Custom — old payloads may have been
        // written with enriched data before the engine was fixed to keep them separate.
        var relevantTokens = tokens.Where(t => t.State != TokenState.Consumed).ToList();
        if (relevantTokens.Count == 0)
            relevantTokens = tokens.OrderByDescending(t => t.ConsumedAt ?? t.CreatedAt).Take(1).ToList();

        var seenInstanceKeys = new HashSet<string>();
        foreach (var token in relevantTokens.OrderByDescending(t => t.CreatedAt))
        {
            foreach (var kvp in DeserializePayload(token.Payload))
                if (!externalKeys.Contains(kvp.Key) && seenInstanceKeys.Add(kvp.Key))
                    rows.Add(new VariableRow("Instance", kvp.Key, FormatValue(kvp.Value)));
        }

        return rows;
    }

    private static Dictionary<string, object?> DeserializePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return [];
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, object?>>(payload);
            if (raw == null) return [];
            var result = new Dictionary<string, object?>(raw.Count);
            foreach (var kvp in raw)
                result[kvp.Key] = UnwrapValue(kvp.Value);
            return result;
        }
        catch { return []; }
    }

    private static object? UnwrapValue(object? value)
    {
        if (value is not JsonElement el) return value;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => el.ToString()
        };
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "(null)",
        string s => s,
        _ => value.ToString() ?? "(null)"
    };
}
