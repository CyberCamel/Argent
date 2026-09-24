using Argent.Core.Authorization;
using Argent.Core.Forms;
using Argent.Core.Forms.Protocol.V2;
using Argent.Infrastructure.Data;
using Argent.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Argent.Runtime.Forms.Stores;

public class EfFormDesignerStore(
    IDbContextFactory<ArgentDbContext> _dbFactory,
    IResourceOwnershipService _ownershipService) : IFormDesignerStore
{
    public async Task<FormDesignerLoadResult?> LoadAsync(Guid formDesignId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var doc = await db.FormDesigns.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == formDesignId);
        if (doc == null) return null;

        var draft = await db.FormDesignDrafts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.FormDesignId == formDesignId);

        var versions = await db.FormDesignVersions.AsNoTracking()
            .Where(v => v.FormDesignId == formDesignId)
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .ToListAsync();

        return new FormDesignerLoadResult
        {
            FormDesignId = formDesignId,
            Name = doc.Name,
            Description = doc.Description,
            // Publishing removes the mutable draft. Keep the designer anchored to the last
            // published snapshot after reload instead of presenting a new empty definition.
            Definition = draft?.Definition ?? versions.FirstOrDefault()?.Definition,
            DraftId = draft?.Id,
            IsReadOnlyVersion = false,
            Versions = versions
        };
    }

    public async Task<FormDesignerLoadResult?> LoadVersionAsync(Guid versionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var version = await db.FormDesignVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId);
        if (version == null) return null;

        var doc = await db.FormDesigns.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == version.FormDesignId);

        var draftId = await db.FormDesignDrafts.AsNoTracking()
            .Where(d => d.FormDesignId == version.FormDesignId)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync();

        var versions = await db.FormDesignVersions.AsNoTracking()
            .Where(v => v.FormDesignId == version.FormDesignId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();

        return new FormDesignerLoadResult
        {
            FormDesignId = version.FormDesignId,
            Name = doc?.Name ?? string.Empty,
            Description = doc?.Description ?? string.Empty,
            Definition = version.Definition,
            DraftId = draftId,
            IsReadOnlyVersion = true,
            Versions = versions
        };
    }

    public async Task<FormDesignerSaveResult> SaveAsync(FormDesignerSaveRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var definitionCopy = Clone(request.Definition);
        var now = DateTime.UtcNow;
        var updatedBy = request.UserName ?? "Unknown";

        Guid formDesignId;
        bool isNew = false;

        if (request.FormDesignId.HasValue)
        {
            formDesignId = request.FormDesignId.Value;
            var existing = await db.FormDesigns.FindAsync(formDesignId);
            if (existing != null)
            {
                existing.Name = request.Name;
                existing.Description = request.Description;
                existing.ObjectKey = definitionCopy.ObjectKey;
                existing.UpdatedAt = now;
            }
        }
        else
        {
            isNew = true;
            var doc = new FormDesign
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                ObjectKey = definitionCopy.ObjectKey,
                CreatedBy = updatedBy
            };
            db.FormDesigns.Add(doc);
            formDesignId = doc.Id;
        }

        var draft = await db.FormDesignDrafts
            .FirstOrDefaultAsync(d => d.FormDesignId == formDesignId);

        Guid draftId;
        if (draft != null)
        {
            draft.Definition = definitionCopy;
            draft.UpdatedAt = now;
            draft.UpdatedBy = updatedBy;
            draftId = draft.Id;
        }
        else
        {
            draft = new FormDesignDraft
            {
                FormDesignId = formDesignId,
                Definition = definitionCopy,
                UpdatedBy = updatedBy
            };
            db.FormDesignDrafts.Add(draft);
            draftId = draft.Id;
        }

        await db.SaveChangesAsync();

        if (isNew && !string.IsNullOrEmpty(request.UserIdentityId))
            await _ownershipService.GrantOwnershipAsync("Form", formDesignId, request.UserIdentityId);

        return new FormDesignerSaveResult { FormDesignId = formDesignId, DraftId = draftId };
    }

    public async Task<FormDesignVersion> PublishAsync(FormPublishRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var draft = await db.FormDesignDrafts
            .FirstOrDefaultAsync(d => d.FormDesignId == request.FormDesignId)
            ?? throw new InvalidOperationException("No draft found to publish.");

        var latestVersion = await db.FormDesignVersions
            .Where(v => v.FormDesignId == request.FormDesignId)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        var nextVersion = latestVersion == null
            ? new Version(1, 0)
            : new Version(latestVersion.Version.Major, latestVersion.Version.Minor + 1);

        var version = new FormDesignVersion
        {
            FormDesignId = request.FormDesignId,
            Version = nextVersion,
            Definition = Clone(draft.Definition),
            CreatedBy = request.UserId ?? "Unknown"
        };
        db.FormDesignVersions.Add(version);
        db.FormDesignDrafts.Remove(draft);
        await db.SaveChangesAsync();

        return version;
    }

    public async Task<FormDesignerLoadResult?> CreateDraftFromVersionAsync(Guid versionId, string? userName = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var version = await db.FormDesignVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId);
        if (version == null) return null;

        var now = DateTime.UtcNow;
        var definitionCopy = Clone(version.Definition);

        var draft = await db.FormDesignDrafts
            .FirstOrDefaultAsync(d => d.FormDesignId == version.FormDesignId);

        if (draft != null)
        {
            draft.Definition = definitionCopy;
            draft.UpdatedAt = now;
            draft.UpdatedBy = userName ?? "Unknown";
        }
        else
        {
            db.FormDesignDrafts.Add(new FormDesignDraft
            {
                FormDesignId = version.FormDesignId,
                Definition = definitionCopy,
                UpdatedBy = userName ?? "Unknown"
            });
        }
        await db.SaveChangesAsync();

        return await LoadAsync(version.FormDesignId);
    }

    public async Task<IReadOnlyList<FormDesignSummary>> GetSummariesByObjectKeyAsync(string objectKey)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FormDesigns.AsNoTracking()
            .Where(d => d.ObjectKey == objectKey)
            .OrderBy(d => d.Name)
            .Select(d => new FormDesignSummary(d.Id, d.Name))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<string>> GetPublishedFieldNamesByObjectKeyAsync(string objectKey)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var formDesignIds = await db.FormDesigns.AsNoTracking()
            .Where(d => d.ObjectKey == objectKey)
            .Select(d => d.Id)
            .ToListAsync();

        if (formDesignIds.Count == 0) return [];

        var latestVersions = new List<FormDesignVersion>();
        foreach (var id in formDesignIds)
        {
            var v = await db.FormDesignVersions.AsNoTracking()
                .Where(v => v.FormDesignId == id)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync();
            if (v != null) latestVersions.Add(v);
        }

        var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in latestVersions)
            CollectFieldNames(v.Definition.Components, fieldNames);

        return [.. fieldNames.Order()];
    }

    private static void CollectFieldNames(IEnumerable<FormComponent> components, HashSet<string> names)
    {
        foreach (var c in components)
        {
            if (c is FormField f && !string.IsNullOrEmpty(f.Name))
                names.Add(f.Name);
            if (c is FormLayout l)
                CollectFieldNames(l.Children, names);
        }
    }

    private static FormDefinition Clone(FormDefinition definition)
    {
        var json = JsonSerializer.Serialize(definition, FormSerializer.Options);
        return JsonSerializer.Deserialize<FormDefinition>(json, FormSerializer.Options) ?? definition;
    }
}
