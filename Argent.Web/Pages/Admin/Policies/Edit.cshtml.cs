using Argent.Infrastructure.Data;
using Argent.Models.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Admin.Policies;

[Authorize(Policy = "SuperAdminOnly")]
public class EditModel(ArgentDbContext _ctx) : PageModel
{
    [FromRoute]
    public Guid Id { get; set; }

    public PolicyDocument? Policy { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Policy = await _ctx.PolicyDocuments.FirstOrDefaultAsync(p => p.Id == Id);
        if (Policy == null) return NotFound();
        return Page();
    }
}
