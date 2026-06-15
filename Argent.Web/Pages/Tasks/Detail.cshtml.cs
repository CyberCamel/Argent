using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Tasks;

[Authorize]
public class DetailModel : PageModel
{
    [FromRoute]
    public Guid Id { get; set; }

    public string? CurrentUserId { get; set; }

    public List<string> CurrentRoles { get; set; } = [];

    public IActionResult OnGet()
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        CurrentRoles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return Page();
    }
}
