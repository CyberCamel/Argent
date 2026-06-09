using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Workflows.Model
{
    [Authorize(Policy = "FlowAdminOnly")]
    public class CreateModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
