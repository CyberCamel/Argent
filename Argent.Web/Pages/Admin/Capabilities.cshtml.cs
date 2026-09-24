using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Admin;

[Authorize(Policy = "SuperAdminOnly")]
public class CapabilitiesModel : PageModel
{
    public void OnGet() { }
}
