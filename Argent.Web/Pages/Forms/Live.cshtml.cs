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
        var doc = await _ctx.FormDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == Id);

        if (doc?.Definition == null)
            return NotFound();

        FormName = doc.Name;
        return Page();
    }
}
