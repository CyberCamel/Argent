using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Workflows
{
    [Authorize(Policy = "FlowAdminOnly")]
    public class ModelerModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
