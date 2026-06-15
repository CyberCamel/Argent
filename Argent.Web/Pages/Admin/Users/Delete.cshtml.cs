using Argent.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.UserAdministration;

[Authorize(Policy = "UserAdminOnly")]
public class DeleteModel(UserManager<InternalUser> userManager) : PageModel
{
    public async Task<IActionResult> OnPostAsync(string id)
    {
        var user = await userManager.FindByNameAsync(id);
        if (user == null) return NotFound();

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return BadRequest(string.Join(", ", result.Errors.Select(e => e.Description)));

        Response.Headers["HX-Trigger"] = "userDeleted";
        return new OkResult();
    }
}
