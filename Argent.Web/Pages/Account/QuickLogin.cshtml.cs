using Argent.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Account;

public class QuickLoginModel(SignInManager<InternalUser> signInManager) : PageModel
{
    public async Task<IActionResult> OnPostAsync(string username, string password, bool rememberMe, string? returnUrl)
    {
        var result = await signInManager.PasswordSignInAsync(username, password, rememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
            return LocalRedirect(returnUrl ?? "/");

        TempData["LoginError"] = "Invalid email or password.";
        return LocalRedirect(returnUrl ?? "/UserAdministration");
    }
}
