using Argent.Contracts.Workflows;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Workflows;

[Authorize(Policy = "FlowAdminOnly")]
public class IndexModel(ArgentDbContext _ctx) : PageModel
{
    public List<WorkflowListItemDto> Defs { get; set; }

    public async Task<IActionResult> OnGet()
    {
        Defs = await _ctx.WorkflowDefinitions.Select(workflow => new WorkflowListItemDto()
            { Id=workflow.Id, Name = workflow.Name, Description = workflow.Description }).ToListAsync();
        return Page();
    }

    public IActionResult OnPostCreate([FromForm] string name, [FromForm] string? description)
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
        _ctx.SaveChanges();

        return RedirectToPage("/Workflows/Model/Edit", new { id = workflow.Id });
    }
}
