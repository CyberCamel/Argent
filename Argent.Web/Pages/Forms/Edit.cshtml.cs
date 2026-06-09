using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Forms;

public class EditModel : PageModel
{
    [FromRoute]
    public Guid Id { get; set; }

    public Guid FormId => Id;

    public IActionResult OnGet()
    {
        return Page();
    }
}
