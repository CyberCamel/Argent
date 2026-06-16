using System.Security.Claims;
using Argent.Contracts.Authorization;
using Argent.Contracts.Workflows;
using Argent.Infrastructure.Data;
using Argent.Models.Enums;
using Argent.Models.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Workflows;

[Authorize(Policy = "FlowAdminOnly")]
public class IndexModel(ArgentDbContext _ctx, IResourceOwnershipService _ownership) : PageModel
{
    public List<WorkflowListItemDto> Defs { get; set; } = [];
    public HashSet<Guid> DeployedWorkflowIds { get; set; } = [];

    public async Task<IActionResult> OnGet()
    {
        Defs = await _ctx.WorkflowDefinitions.Select(workflow => new WorkflowListItemDto()
            { Id=workflow.Id, Name = workflow.Name, Description = workflow.Description }).ToListAsync();

        DeployedWorkflowIds = (await _ctx.WorkflowVersions
            .Where(v => v.State == WorkflowDefinitionState.Deployed)
            .Select(v => v.WorkflowId)
            .ToListAsync())
            .ToHashSet();

        return Page();
    }

    public async Task<IActionResult> OnPostCreate([FromForm] string name, [FromForm] string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError("name", "Name is required.");
            return Page();
        }

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description?.Trim() ?? "",
            CreatedOn = DateTime.UtcNow,
            UpdatedOn = DateTime.UtcNow,
            Tags = []
        };
        _ctx.WorkflowDefinitions.Add(workflow);
        await _ctx.SaveChangesAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (!string.IsNullOrEmpty(userId))
            await _ownership.GrantOwnershipAsync("Workflow", workflow.Id, userId);

        return RedirectToPage("/Workflows/Model/Edit", new { id = workflow.Id });
    }
    public async Task<IActionResult> OnPostDelete(Guid id)
    {
        var workflow = await _ctx.WorkflowDefinitions.FindAsync(id);
        if (workflow != null)
        {
            _ctx.WorkflowDefinitions.Remove(workflow);
            await _ctx.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
