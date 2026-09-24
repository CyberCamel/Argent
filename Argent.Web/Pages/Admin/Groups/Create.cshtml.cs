using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Admin.Groups;

[Authorize(Policy = "SuperAdminOnly")]
public class CreateModel : PageModel
{
    public void OnGet() { }
}
