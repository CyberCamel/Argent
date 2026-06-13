using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.DomainObjects;

public class EditModel : PageModel
{
    [FromRoute]
    public Guid Id { get; set; }

    public Guid ObjectId => Id;

    public IActionResult OnGet() => Page();
}
