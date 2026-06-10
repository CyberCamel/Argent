using Argent.Contracts.Workflows;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Workflows;
public class IndexModel(ApplicationDbContext _ctx) : PageModel
{
    public List<WorkflowListItemDto> Defs { get; set; }

    public async Task<IActionResult> OnGet()
    {
        Defs = await _ctx.WorkflowDefinitions.Select(workflow => new WorkflowListItemDto()
            { Id=workflow.Id, Name = workflow.Name, Description = workflow.Description }).ToListAsync();
        return Page();
    }
}

