using Argent.Infrastructure.Data;
using Argent.Models.Enums;
using Argent.Models.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Workflows;

[Authorize]
public class StartModel(ArgentDbContext _ctx) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? WorkflowId { get; set; }

    // List mode
    public List<StartableWorkflowDto> Workflows { get; set; } = [];

    // Form mode
    public string WorkflowName { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        if (WorkflowId.HasValue)
        {
            var workflow = await _ctx.WorkflowDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == WorkflowId.Value);

            if (workflow == null)
                return NotFound();

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

        Workflows = deployedVersions
            .Select(x => new StartableWorkflowDto
            {
                Id = x.Id,
                Name = x.Name,
                Description = x.Description
            })
            .ToList();

        return Page();
    }

    public class StartableWorkflowDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
