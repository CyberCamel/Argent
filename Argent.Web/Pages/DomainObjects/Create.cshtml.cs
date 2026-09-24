using Argent.WebComponents.DomainObjects.Designer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.DomainObjects;

public class CreateModel(DomainObjectDesignerService _designer) : PageModel
{
    public IActionResult OnGet()
    {
        _designer.Reset();
        return Page();
    }
}
