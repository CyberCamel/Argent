using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Admin.WorkflowInstances;

[Authorize(Policy = "FlowAdminOnly")]
public class IndexModel(ArgentDbContext _ctx) : PageModel
{
    public List<InstanceListItem> Instances { get; set; } = [];

    public async Task<IActionResult> OnGet()
    {
        var instances = await _ctx.WorkflowInstances
            .AsNoTracking()
            .OrderByDescending(i => i.StartTime)
            .ToListAsync();

        var instanceIds = instances.Select(i => i.InstanceId).ToList();

        var activeTokenCounts = await _ctx.WorkflowTokens
            .Where(t => instanceIds.Contains(t.InstanceId) && t.State != TokenState.Consumed)
            .GroupBy(t => t.InstanceId)
            .Select(g => new { InstanceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.InstanceId, g => g.Count);

        Instances = instances.Select(i => new InstanceListItem
        {
            InstanceId = i.InstanceId,
            WorkflowId = i.WorkflowId,
            WorkflowName = i.Name,
            State = i.State,
            StartTime = i.StartTime,
            EndTime = i.EndTime,
            RecordId = i.RecordId,
            TokenCount = activeTokenCounts.GetValueOrDefault(i.InstanceId, 0)
        }).ToList();

        return Page();
    }

    public record InstanceListItem
    {
        public required Guid InstanceId { get; init; }
        public required Guid WorkflowId { get; init; }
        public required string WorkflowName { get; init; }
        public required InstanceState State { get; init; }
        public required DateTime StartTime { get; init; }
        public DateTime? EndTime { get; init; }
        public Guid RecordId { get; init; }
        public required int TokenCount { get; init; }
    }
}
