using Argent.Infrastructure.Data;
using Argent.Models.Forms.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Forms;

public class LiveModel : PageModel
{
    private readonly ApplicationDbContext _ctx;

    public LiveModel(ApplicationDbContext ctx)
    {
        _ctx = ctx;
    }

    [FromRoute]
    public Guid Id { get; set; }

    public string FormName { get; set; } = string.Empty;
    public FormDefinition? FormDefinition { get; set; }
    public string? SubmittedMessage { get; set; }

    public async Task<IActionResult> OnGet()
    {
        var doc = await _ctx.FormDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == Id);

        if (doc?.Definition == null)
            return NotFound();

        FormName = doc.Name;
        FormDefinition = doc.Definition;
        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        var doc = await _ctx.FormDocuments
            .FirstOrDefaultAsync(f => f.Id == Id);

        if (doc?.Definition == null)
            return NotFound();

        FormName = doc.Name;
        FormDefinition = doc.Definition;
        SubmittedMessage = "Form submitted successfully.";
        return Page();
    }
}
