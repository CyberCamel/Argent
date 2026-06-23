using Argent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Forms;

public class LiveModel(ArgentDbContext _ctx) : PageModel
{
    [FromRoute]
    public Guid Id { get; set; }

    public string FormName { get; set; } = string.Empty;

    public async Task<IActionResult> OnGet()
    {
        var doc = await _ctx.FormDesigns
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == Id);

        if (doc == null)
            return NotFound();

        // Require at least a draft or a published version to consider the form usable
        var hasDraft = await _ctx.FormDesignDrafts.AnyAsync(d => d.FormDesignId == Id);
        var hasVersion = await _ctx.FormDesignVersions.AnyAsync(v => v.FormDesignId == Id);
        if (!hasDraft && !hasVersion)
            return NotFound();

        FormName = doc.Name;
        return Page();
    }
}
