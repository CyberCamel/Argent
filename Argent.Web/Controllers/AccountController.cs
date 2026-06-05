using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Argent.Models.Identity;

namespace Argent.Web.Controllers;


public class AccountController : Controller
{
    private readonly UserManager<InternalUser> _userManager;
    private readonly SignInManager<InternalUser> _signInManager;

    public AccountController(UserManager<InternalUser> userManager, SignInManager<InternalUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickLogin(string username, string password, bool rememberMe, string? returnUrl)
    {
        var result = await _signInManager.PasswordSignInAsync(username, password, rememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            return LocalRedirect(returnUrl ?? "/");
        }

        TempData["LoginError"] = "Invalid email or password.";

        return LocalRedirect(returnUrl ?? "/UserAdministration");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();

        return LocalRedirect("/");

    }
}