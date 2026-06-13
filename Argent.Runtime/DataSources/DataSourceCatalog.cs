using System.Text.Json;
using Argent.Contracts.DataSources;
using Argent.Infrastructure.Data;
using Argent.Models.DataSources;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.DataSources;

/// <summary>
/// Admin CRUD over stored data sources. The full connection config (including secrets) is
/// serialized polymorphically and encrypted via <see cref="ISecretProtector"/> before saving;
/// metadata columns stay in the clear for listing.
/// </summary>
public class DataSourceCatalog(
    IDbContextFactory<ArgentDbContext> _dbContextFactory,
    IHttpContextAccessor _httpContextAccessor,
    ISecretProtector _protector) : IDataSourceCatalog
{
    private string CurrentUser => _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Unknown";

    public async Task<List<DataSourceSummary>> GetSummariesAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.DataSources.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DataSourceSummary
            {
                Id = d.Id,
                Key = d.Key,
                Name = d.Name,
                Description = d.Description,
                Kind = d.Kind
            }).ToListAsync();
    }

    public async Task<DataSource?> GetAsync(Guid id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var doc = await dbContext.DataSources.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        return doc is null ? null : Decrypt(doc);
    }

    public async Task<DataSource?> GetByKeyAsync(string key)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var doc = await dbContext.DataSources.AsNoTracking().FirstOrDefaultAsync(d => d.Key == key);
        return doc is null ? null : Decrypt(doc);
    }

    public async Task<Guid> SaveAsync(DataSource dataSource, Guid? id = null, string? user = null)
    {
        if (string.IsNullOrWhiteSpace(dataSource.Key))
            throw new InvalidOperationException("A data source key is required.");
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var duplicate = await dbContext.DataSources
            .AnyAsync(d => d.Key == dataSource.Key && (id == null || d.Id != id));
        if (duplicate)
            throw new InvalidOperationException($"A data source with key '{dataSource.Key}' already exists.");

        var now = DateTime.UtcNow;
        var encrypted = _protector.Protect(JsonSerializer.Serialize(dataSource));

        DataSourceDocument doc;
        if (id is { } existingId)
        {
            doc = await dbContext.DataSources.FirstOrDefaultAsync(d => d.Id == existingId)
                  ?? throw new InvalidOperationException("Data source not found.");
        }
        else
        {
            doc = new DataSourceDocument { CreatedAt = now, CreatedBy = user ?? CurrentUser };
            dbContext.DataSources.Add(doc);
        }

        doc.Key = dataSource.Key;
        doc.Name = dataSource.Name;
        doc.Description = dataSource.Description;
        doc.Kind = dataSource.Kind;
        doc.Config = encrypted;
        doc.UpdatedAt = now;

        await dbContext.SaveChangesAsync();
        return doc.Id;
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var doc = await dbContext.DataSources.FindAsync(id);
        if (doc is null) return;
        dbContext.DataSources.Remove(doc);
        await dbContext.SaveChangesAsync();
    }

    private DataSource Decrypt(DataSourceDocument doc) =>
        JsonSerializer.Deserialize<DataSource>(_protector.Unprotect(doc.Config))
        ?? throw new InvalidOperationException($"Data source '{doc.Key}' could not be deserialized.");
}
