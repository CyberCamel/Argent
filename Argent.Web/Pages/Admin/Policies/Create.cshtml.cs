using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Admin.Policies;

[Authorize(Policy = "SuperAdminOnly")]
public class CreateModel : PageModel
{
    public void OnGet() { }
}
