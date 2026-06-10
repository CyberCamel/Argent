using Argent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Workflows;

public class ViewModel(ApplicationDbContext _ctx) : PageModel
{
    public Guid WorkflowId { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    public string WorkflowDescription { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var workflow = await _ctx.WorkflowDefinitions
            .Where(w => w.Id == id)
            .Select(w => new { w.Name, w.Description })
            .FirstOrDefaultAsync();

        if (workflow == null) return NotFound();

        WorkflowId = id;
        WorkflowName = workflow.Name;
        WorkflowDescription = workflow.Description;

        return Page();
    }
}
