using Argent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _ctx;

    public IndexModel(ApplicationDbContext ctx)
    {
        _ctx = ctx;
    }

    public int WorkflowCount { get; set; }
    public int FormCount { get; set; }
    public List<WorkflowItem> RecentWorkflows { get; set; } = [];
    public List<FormItem> RecentForms { get; set; } = [];

    public async Task OnGet()
    {
        WorkflowCount = await _ctx.WorkflowDefinitions.CountAsync();
        RecentWorkflows = await _ctx.WorkflowDefinitions
            .OrderByDescending(w => w.Id)
            .Take(5)
            .Select(w => new WorkflowItem { Id = w.Id, Name = w.Name, Description = w.Description })
            .ToListAsync();

        FormCount = await _ctx.FormDocuments.CountAsync();
        RecentForms = await _ctx.FormDocuments
            .OrderByDescending(f => f.UpdatedAt)
            .Take(5)
            .Select(f => new FormItem { Id = f.Id, Name = f.Name, Description = f.Description })
            .ToListAsync();
    }

    public class WorkflowItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class FormItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
