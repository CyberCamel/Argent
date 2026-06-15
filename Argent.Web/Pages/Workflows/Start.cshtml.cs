using System.Security.Claims;
using Argent.Contracts.Authorization;
using Argent.Infrastructure.Data;
using Argent.Models.Authorization;
using Argent.Models.Enums;
using Argent.Models.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Workflows;

[Authorize]
public class StartModel(ArgentDbContext _ctx, IPolicyDecisionService _policyService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? WorkflowId { get; set; }

    // List mode
    public List<StartableWorkflowDto> Workflows { get; set; } = [];

    // Form mode
    public string WorkflowName { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        if (WorkflowId.HasValue)
        {
            var workflow = await _ctx.WorkflowDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == WorkflowId.Value);

            if (workflow == null)
                return NotFound();

            var decision = await _policyService.EvaluateAsync(
                userId, roles, "Workflow",
                new Dictionary<string, object?> { ["id"] = workflow.Id.ToString() },
                ResourceActions.Workflow.Run);

            if (decision != PolicyDecision.Allow)
                return Forbid();

            WorkflowName = workflow.Name;
            return Page();
        }

        var deployedVersions = await _ctx.WorkflowVersions
            .Where(v => v.State == WorkflowDefinitionState.Deployed)
            .Join(_ctx.WorkflowDefinitions,
                v => v.WorkflowId,
                w => w.Id,
                (v, w) => new { v.Definition, w.Id, w.Name, w.Description })
            .ToListAsync();

        // Show only workflows the current user has run access to.
        var visibleWorkflows = new List<StartableWorkflowDto>();
        foreach (var x in deployedVersions)
        {
            var decision = await _policyService.EvaluateAsync(
                userId, roles, "Workflow",
                new Dictionary<string, object?> { ["id"] = x.Id.ToString() },
                ResourceActions.Workflow.Run);

            if (decision == PolicyDecision.Allow)
                visibleWorkflows.Add(new StartableWorkflowDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Description = x.Description
                });
        }

        Workflows = visibleWorkflows;
        return Page();
    }

    public class StartableWorkflowDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
