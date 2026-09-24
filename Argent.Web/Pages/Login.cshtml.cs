using Argent.Core;
using Argent.Core.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;


namespace Argent.Web.Pages;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<InternalUser> _signInManager;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public LoginModel(SignInManager<InternalUser> signInManager, IStringLocalizer<SharedResource> localizer)
    {
        _signInManager = signInManager;
        _localizer = localizer;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (ModelState.IsValid)
        {
            var result = await _signInManager.PasswordSignInAsync(Input.UserName, Input.Password, Input.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded) return LocalRedirect(returnUrl);
            if (result.IsLockedOut) ModelState.AddModelError(string.Empty, _localizer["Login.LockedOut"]);

            ModelState.AddModelError(string.Empty, _localizer["Login.InvalidAttempt"]);
        }
        return Page();
    }

    public class InputModel
    {
        [Required] public string UserName { get; set; } = "";
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
        public bool RememberMe { get; set; } = false;
    }
}