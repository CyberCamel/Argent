using Argent.Contracts.Branding;
using Argent.Infrastructure.Data;
using Argent.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Branding;

public class BrandingService(
    IDbContextFactory<ArgentDbContext> _dbContextFactory,
    IHttpContextAccessor _httpContextAccessor) : IBrandingService
{
    private static BrandingSettings? _cached;
    private static readonly object _lock = new();

    private string CurrentUser => _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Unknown";

    public async Task<BrandingSettings?> GetAsync()
    {
        if (_cached is not null)
            return _cached;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var settings = await dbContext.BrandingSettings.AsNoTracking().FirstOrDefaultAsync();

        if (settings is not null)
        {
            lock (_lock) { _cached = settings; }
        }

        return settings;
    }

    public async Task SaveAsync(BrandingSettings settings, string? user = null)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var existing = await dbContext.BrandingSettings.FirstOrDefaultAsync();
        if (existing is null)
        {
            settings.Id = Guid.NewGuid();
            dbContext.BrandingSettings.Add(settings);
        }
        else
        {
            existing.SiteName = settings.SiteName;
            existing.LogoUrl = settings.LogoUrl;
            existing.FaviconUrl = settings.FaviconUrl;
            existing.PrimaryColor = settings.PrimaryColor;
            existing.PrimaryHoverColor = settings.PrimaryHoverColor;
            existing.FooterText = settings.FooterText;
            existing.CustomCss = settings.CustomCss;
            existing.IsEnabled = settings.IsEnabled;
        }

        await dbContext.SaveChangesAsync();
        InvalidateCache();
    }

    public void InvalidateCache()
    {
        lock (_lock) { _cached = null; }
    }
}
