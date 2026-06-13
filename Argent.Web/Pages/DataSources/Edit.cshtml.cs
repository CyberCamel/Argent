using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.DataSources;

[Authorize(Roles = "SuperAdmin")]
public class EditModel : PageModel
{
    [FromRoute]
    public Guid Id { get; set; }

    public IActionResult OnGet() => Page();
}
