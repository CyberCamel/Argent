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
    public List<StartableWorkflowDto> Workflows { get; set; } = [];

    public async Task OnGetAsync()
    {
        var deployedVersions = await _ctx.WorkflowVersions
            .Where(v => v.State == WorkflowDefinitionState.Deployed)
            .Join(_ctx.WorkflowDefinitions,
                v => v.WorkflowId,
                w => w.Id,
                (v, w) => new StartableWorkflowDto
                {
                    Id = w.Id,
                    Name = w.Name,
                    Description = w.Description,
                    FormId = ExtractFormId(v.Definition)
                })
            .Where(d => d.FormId.HasValue)
            .ToListAsync();

        Workflows = deployedVersions;
    }

    private static Guid? ExtractFormId(WorkflowDefinition? def)
    {
        var startNode = def?.Nodes?.OfType<StartEvent>().FirstOrDefault();
        return startNode?.FormId;
    }

    public class StartableWorkflowDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Guid? FormId { get; set; }
    }
}
