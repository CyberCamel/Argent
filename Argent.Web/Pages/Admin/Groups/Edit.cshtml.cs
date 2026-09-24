using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Admin.Groups;

[Authorize(Policy = "SuperAdminOnly")]
public class EditModel : PageModel
{
    [FromRoute]
    public Guid Id { get; set; }

    public IActionResult OnGet() => Page();
}
