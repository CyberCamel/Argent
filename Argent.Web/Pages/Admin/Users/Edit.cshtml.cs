using Argent.Models.Identity;
using Argent.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.UserAdministration;

[Authorize(Policy = "UserAdminOnly")]
public class EditModel(
    UserManager<InternalUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager) : PageModel
{
    [BindProperty]
    public EditUserViewModel EditUser { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var user = await userManager.FindByNameAsync(id);
        if (user == null) return NotFound();

        EditUser = new EditUserViewModel
        {
            UserName = user.UserName ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Roles = [.. (await userManager.GetRolesAsync(user))],
            AvailableRoles = [.. roleManager.Roles.Select(r => r.Name ?? "Unknown role")]
        };

        return Partial("~/Pages/Admin/Users/_EditUserPartial.cshtml", EditUser);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Partial("~/Pages/Admin/Users/_EditUserPartial.cshtml", EditUser);

        var user = await userManager.FindByNameAsync(EditUser.UserName);
        if (user == null) return NotFound();

        user.FirstName = EditUser.FirstName;
        user.LastName = EditUser.LastName;
        user.Email = EditUser.Email;

        var result = await userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return Partial("~/Pages/Admin/Users/_EditUserPartial.cshtml", EditUser);
        }

        Response.Headers["HX-Trigger"] = "userActionCompleted";
        return new OkResult();
    }
}
