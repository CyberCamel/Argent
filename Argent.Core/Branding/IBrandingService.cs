using Argent.Core;

namespace Argent.Core.Branding;

public interface IBrandingService
{
    Task<BrandingSettings?> GetAsync();
    Task SaveAsync(BrandingSettings settings, string? user = null);
    void InvalidateCache();
}
