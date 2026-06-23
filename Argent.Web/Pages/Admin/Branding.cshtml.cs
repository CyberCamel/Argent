using Argent.Contracts.Branding;
using Argent.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Admin;

[Authorize(Policy = "SuperAdminOnly")]
public class BrandingModel(IBrandingService _branding) : PageModel
{
    [BindProperty]
    public string SiteName { get; set; } = "Argent";

    [BindProperty]
    public string? LogoUrl { get; set; }

    [BindProperty]
    public string? FaviconUrl { get; set; }

    [BindProperty]
    public string PrimaryColor { get; set; } = "#4f46e5";

    [BindProperty]
    public string PrimaryHoverColor { get; set; } = "#4338ca";

    [BindProperty]
    public string? FooterText { get; set; }

    [BindProperty]
    public string? CustomCss { get; set; }

    [BindProperty]
    public bool IsEnabled { get; set; } = true;

    public bool Saved { get; set; }

    public async Task<IActionResult> OnGet()
    {
        var settings = await _branding.GetAsync();
        if (settings is not null)
        {
            SiteName = settings.SiteName;
            LogoUrl = settings.LogoUrl;
            FaviconUrl = settings.FaviconUrl;
            PrimaryColor = settings.PrimaryColor;
            PrimaryHoverColor = settings.PrimaryHoverColor;
            FooterText = settings.FooterText;
            CustomCss = settings.CustomCss;
            IsEnabled = settings.IsEnabled;
        }

        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        if (!ModelState.IsValid)
            return Page();

        var settings = new Argent.Models.BrandingSettings
        {
            SiteName = SiteName,
            LogoUrl = LogoUrl,
            FaviconUrl = FaviconUrl,
            PrimaryColor = PrimaryColor,
            PrimaryHoverColor = PrimaryHoverColor,
            FooterText = FooterText,
            CustomCss = CustomCss,
            IsEnabled = IsEnabled,
        };

        await _branding.SaveAsync(settings);
        Saved = true;

        return Page();
    }
}
