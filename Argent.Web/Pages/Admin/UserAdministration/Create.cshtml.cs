using Argent.Models.Identity;
using Argent.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.UserAdministration;

[Authorize(Policy = "UserAdminOnly")]
public class CreateModel(UserManager<InternalUser> userManager) : PageModel
{
    [BindProperty]
    public CreateUserViewModel CreateUser { get; set; } = new();

    public IActionResult OnGet()
    {
        return Partial("~/Pages/UserAdministration/_CreateUserPartial.cshtml", new CreateUserViewModel());
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = 400;
            return Partial("~/Pages/UserAdministration/_CreateUserPartial.cshtml", CreateUser);
        }

        var result = await userManager.CreateAsync(new InternalUser
        {
            UserName = CreateUser.UserName,
            Email = CreateUser.Email,
            FirstName = CreateUser.FirstName,
            LastName = CreateUser.LastName,
            EmailConfirmed = true
        }, CreateUser.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return Partial("~/Pages/UserAdministration/_CreateUserPartial.cshtml", CreateUser);
        }

        Response.Headers["HX-Trigger"] = "userActionCompleted";
        return new OkResult();
    }
}
