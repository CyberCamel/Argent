using Argent.Models;

namespace Argent.Contracts.Branding;

public interface IBrandingService
{
    Task<BrandingSettings?> GetAsync();
    Task SaveAsync(BrandingSettings settings, string? user = null);
    void InvalidateCache();
}
