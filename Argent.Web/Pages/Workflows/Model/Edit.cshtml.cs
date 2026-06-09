using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Workflows.Model
{
    public class EditModel : PageModel
    {
        public string WorkflowName { get; set; }
        public string WorkflowId { get; set; }
        public void OnGet()
        {
        }
    }
}
