using Argent.Core;
using Argent.Core.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Argent.Web.Pages.Account;

[AllowAnonymous]
public class QuickLoginModel : PageModel
{
    private readonly SignInManager<InternalUser> _signInManager;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly IWebHostEnvironment _env;

    public QuickLoginModel(
        SignInManager<InternalUser> signInManager,
        IStringLocalizer<SharedResource> localizer,
        IWebHostEnvironment env)
    {
        _signInManager = signInManager;
        _localizer = localizer;
        _env = env;
    }

    public IActionResult OnGet()
    {
        if (!_env.IsDevelopment())
            return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string username, string password, bool rememberMe, string? returnUrl)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var result = await _signInManager.PasswordSignInAsync(username, password, rememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
            return LocalRedirect(returnUrl ?? "/");

        TempData["LoginError"] = _localizer["QuickLogin.InvalidCredentials"];
        return LocalRedirect(returnUrl ?? "/UserAdministration");
    }
}
