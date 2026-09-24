using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.DataSources;

[Authorize(Roles = "SuperAdmin")]
public class CreateModel : PageModel
{
    public IActionResult OnGet() => Page();
}
