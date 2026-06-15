using Argent.Runtime.Forms.Modeling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Forms;

[Authorize(Policy = "FormAdminOnly")]
public class CreateModel(FormDesignerService _formDesigner) : PageModel
{
    public IActionResult OnGet()
    {
        _formDesigner.Reset();
        return Page();
    }
}
