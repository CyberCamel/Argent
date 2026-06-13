using System.Text.Json;
using Argent.Contracts.DomainObjects;
using Argent.Infrastructure.Data;
using Argent.Models.DomainObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.DomainObjects;

/// <summary>
/// Design-time authoring of domain objects. Mirrors the workflow draft/version lifecycle:
/// edits live on a single draft per object; publishing snapshots the draft into a new
/// immutable, semver-stamped version and clears the draft.
/// </summary>
public class DomainObjectDefinitionService(
    IDbContextFactory<ArgentDbContext> _dbContextFactory,
    IHttpContextAccessor _httpContextAccessor) : IDomainObjectDefinitionService
{
    private string CurrentUser => _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Unknown";

    public async Task<List<DomainObjectSummary>> GetSummariesAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var objects = await dbContext.DomainObjects.AsNoTracking().ToListAsync();
        var draftIds = await dbContext.DomainObjectDrafts.AsNoTracking()
            .Select(d => d.DomainObjectId).ToListAsync();
        var draftSet = draftIds.ToHashSet();

        // Version is stored as a string, so resolve "latest published" in memory where Version compares correctly.
        var published = await dbContext.DomainObjectVersions.AsNoTracking()
            .Where(v => v.State == DomainObjectState.Published)
            .Select(v => new { v.DomainObjectId, v.Version })
            .ToListAsync();
        var latestByObject = published
            .GroupBy(v => v.DomainObjectId)
            .ToDictionary(g => g.Key, g => g.Max(v => v.Version));

        return objects.Select(o => new DomainObjectSummary
        {
            Id = o.Id,
            Key = o.Key,
            Name = o.Name,
            Description = o.Description,
            HasDraft = draftSet.Contains(o.Id),
            PublishedVersion = latestByObject.TryGetValue(o.Id, out var v) ? v.ToString() : null
        }).ToList();
    }

    public async Task<DomainObject?> GetAsync(Guid id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.DomainObjects.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
    }
        

    public async Task<DomainObject> CreateAsync(string key, string name, string? description = null, string? createdBy = null)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        if (await dbContext.DomainObjects.AnyAsync(o => o.Key == key))
            throw new InvalidOperationException($"A domain object with key '{key}' already exists.");

        var now = DateTime.UtcNow;
        var author = createdBy ?? CurrentUser;

        var domainObject = new DomainObject
        {
            Key = key,
            Name = name,
            Description = description ?? string.Empty,
            CreatedOn = now,
            UpdatedOn = now
        };
        dbContext.DomainObjects.Add(domainObject);

        dbContext.DomainObjectDrafts.Add(new DomainObjectDraft
        {
            DomainObjectId = domainObject.Id,
            Name = name,
            Description = description ?? string.Empty,
            Definition = new DomainObjectDefinition
            {
                Key = key,
                DisplayName = name,
                Description = description
            },
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = author
        });

        await dbContext.SaveChangesAsync();
        return domainObject;
    }

    public async Task<DomainObjectDefinition?> GetWorkingDefinitionAsync(Guid id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var draft = await dbContext.DomainObjectDrafts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DomainObjectId == id);
        if (draft != null) return draft.Definition;

        return await GetLatestPublishedDefinitionAsync(o => o.Id == id);
    }

    public async Task<DomainObjectDefinition?> GetPublishedDefinitionAsync(string key) =>
        await GetLatestPublishedDefinitionAsync(o => o.Key == key);

    public async Task SaveDraftAsync(Guid id, DomainObjectDefinition definition, string? updatedBy = null)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var copy = Clone(definition);

        var draft = await dbContext.DomainObjectDrafts.FirstOrDefaultAsync(d => d.DomainObjectId == id);
        if (draft != null)
        {
            draft.Definition = copy;
            draft.Name = definition.DisplayName;
            draft.Description = definition.Description ?? string.Empty;
            draft.UpdatedAt = now;
        }
        else
        {
            dbContext.DomainObjectDrafts.Add(new DomainObjectDraft
            {
                DomainObjectId = id,
                Name = definition.DisplayName,
                Description = definition.Description ?? string.Empty,
                Definition = copy,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = updatedBy ?? CurrentUser
            });
        }

        var header = await dbContext.DomainObjects.FirstOrDefaultAsync(o => o.Id == id);
        if (header != null) header.UpdatedOn = now;

        await dbContext.SaveChangesAsync();
    }

    public async Task<DomainObjectVersion> PublishAsync(Guid id, string? createdBy = null)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var draft = await dbContext.DomainObjectDrafts.FirstOrDefaultAsync(d => d.DomainObjectId == id)
            ?? throw new InvalidOperationException("There is no draft to publish for this domain object.");

        var existingVersions = await dbContext.DomainObjectVersions.AsNoTracking()
            .Where(v => v.DomainObjectId == id)
            .Select(v => v.Version)
            .ToListAsync();
        var nextVersion = existingVersions.Count == 0
            ? new Version(1, 0)
            : new Version(existingVersions.Max()!.Major, existingVersions.Max()!.Minor + 1);

        var version = new DomainObjectVersion
        {
            DomainObjectId = id,
            Version = nextVersion,
            Name = draft.Name,
            Description = draft.Description,
            Definition = Clone(draft.Definition),
            State = DomainObjectState.Published,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy ?? CurrentUser
        };
        dbContext.DomainObjectVersions.Add(version);
        dbContext.DomainObjectDrafts.Remove(draft);

        await dbContext.SaveChangesAsync();
        return version;
    }

    public async Task<List<DomainObjectVersion>> GetVersionsAsync(Guid id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var versions = await dbContext.DomainObjectVersions.AsNoTracking()
            .Where(v => v.DomainObjectId == id)
            .ToListAsync();
        return versions.OrderByDescending(v => v.Version).ToList();
    }

    private async Task<DomainObjectDefinition?> GetLatestPublishedDefinitionAsync(
        System.Linq.Expressions.Expression<Func<DomainObject, bool>> predicate)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var objectId = await dbContext.DomainObjects.AsNoTracking()
            .Where(predicate).Select(o => (Guid?)o.Id).FirstOrDefaultAsync();
        if (objectId is null) return null;

        var versions = await dbContext.DomainObjectVersions.AsNoTracking()
            .Where(v => v.DomainObjectId == objectId && v.State == DomainObjectState.Published)
            .ToListAsync();

        return versions.OrderByDescending(v => v.Version).FirstOrDefault()?.Definition;
    }

    private static DomainObjectDefinition Clone(DomainObjectDefinition definition) =>
        JsonSerializer.Deserialize<DomainObjectDefinition>(JsonSerializer.Serialize(definition)) ?? definition;
}
