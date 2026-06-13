using Argent.Contracts.DomainObjects;
using Argent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.DomainObjects;

public class IndexModel(IDomainObjectDefinitionService _definitions, ArgentDbContext _ctx) : PageModel
{
    public List<DomainObjectSummary> Objects { get; set; } = [];

    public async Task<IActionResult> OnGet()
    {
        Objects = await _definitions.GetSummariesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDelete(Guid id)
    {
        var domainObject = await _ctx.DomainObjects.FindAsync(id);
        if (domainObject != null)
        {
            // Cascade removes versions, drafts, and records.
            _ctx.DomainObjects.Remove(domainObject);
            await _ctx.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
