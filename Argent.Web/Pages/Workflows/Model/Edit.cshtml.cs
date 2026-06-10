using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Workflows.Model
{
    public class EditModel : PageModel
    {
        public string WorkflowName { get; set; } = "";
        public Guid WorkflowId { get; set; }

        public async Task<IActionResult> OnGet([FromRoute] Guid id)
        {
            var ctx = HttpContext.RequestServices.GetRequiredService<IDbContextFactory<ArgentDbContext>>();
            await using var db = await ctx.CreateDbContextAsync();
            var workflow = await db.WorkflowDefinitions.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
            if (workflow == null) return NotFound();

            WorkflowId = id;
            WorkflowName = workflow.Name;
            return Page();
        }
    }
}
