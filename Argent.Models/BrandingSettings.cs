using System.ComponentModel.DataAnnotations;

namespace Argent.Models;

public class BrandingSettings
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(128)]
    public string SiteName { get; set; } = "Argent";

    [MaxLength(512)]
    public string? LogoUrl { get; set; }

    [MaxLength(512)]
    public string? FaviconUrl { get; set; }

    [MaxLength(16)]
    public string PrimaryColor { get; set; } = "#4f46e5";

    [MaxLength(16)]
    public string PrimaryHoverColor { get; set; } = "#4338ca";

    [MaxLength(256)]
    public string? FooterText { get; set; }

    public string? CustomCss { get; set; }

    public bool IsEnabled { get; set; } = true;
}
