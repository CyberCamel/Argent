using Argent.Core.Workflows.Designer;
using Argent.Core.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Core.Enums;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Workflows.Stores;

public class EfWorkflowDesignerStore(
    IDbContextFactory<ArgentDbContext> _dbFactory,
    IAuditService _auditService) : IWorkflowDesignerStore
{
    public async Task<WorkflowDesignerLoadResult?> LoadWorkflowAsync(Guid workflowId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var workflow = await db.Workflows.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowId);
        if (workflow == null) return null;

        var draft = await db.WorkflowDrafts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.WorkflowId == workflowId);

        if (draft != null)
        {
            return new WorkflowDesignerLoadResult
            {
                WorkflowId = workflowId,
                Name = draft.Name,
                Description = draft.Description,
                Definition = draft.Definition,
                DraftId = draft.Id
            };
        }

        var version = await db.WorkflowVersions.AsNoTracking()
            .Where(v => v.WorkflowId == workflowId && v.State == WorkflowDefinitionState.Deployed)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync()
            ?? await db.WorkflowVersions.AsNoTracking()
                .Where(v => v.WorkflowId == workflowId)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync();

        if (version != null)
        {
            return new WorkflowDesignerLoadResult
            {
                WorkflowId = workflowId,
                Name = version.Name,
                Description = version.Description,
                Definition = version.Definition,
                VersionId = version.Id,
                VersionRoleAudiences = version.RoleAudiences
            };
        }

        return new WorkflowDesignerLoadResult
        {
            WorkflowId = workflowId,
            Name = workflow.Name,
            Description = workflow.Description
        };
    }

    public async Task<WorkflowDesignerLoadResult?> LoadVersionAsync(Guid versionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var version = await db.WorkflowVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId);
        if (version == null) return null;

        return new WorkflowDesignerLoadResult
        {
            WorkflowId = version.WorkflowId,
            Name = version.Name,
            Description = version.Description,
            Definition = version.Definition,
            VersionId = version.Id,
            VersionRoleAudiences = version.RoleAudiences
        };
    }

    public async Task<WorkflowSaveDraftResult> SaveDraftAsync(WorkflowSaveDraftRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        Guid workflowId;
        if (request.WorkflowId.HasValue)
        {
            workflowId = request.WorkflowId.Value;
            var wf = await db.Workflows.FindAsync(workflowId);
            if (wf != null)
            {
                wf.Name = request.Name;
                wf.Description = request.Description;
                wf.UpdatedOn = now;
            }
        }
        else
        {
            var wf = new Workflow
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                CreatedOn = now,
                UpdatedOn = now,
                Tags = []
            };
            db.Workflows.Add(wf);
            workflowId = wf.Id;
        }

        var draft = request.ExistingDraftId.HasValue
            ? await db.WorkflowDrafts.FindAsync(request.ExistingDraftId.Value)
            : await db.WorkflowDrafts.FirstOrDefaultAsync(d => d.WorkflowId == workflowId);

        Guid draftId;
        if (draft != null)
        {
            draft.Definition = request.Definition;
            draft.Name = request.Name;
            draft.Description = request.Description;
            draft.UpdatedAt = now;
            draftId = draft.Id;
        }
        else
        {
            draft = new WorkflowDraft
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflowId,
                Name = request.Name,
                Description = request.Description,
                Definition = request.Definition,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = request.UserId ?? "Unknown"
            };
            db.WorkflowDrafts.Add(draft);
            draftId = draft.Id;
        }

        await db.SaveChangesAsync();
        return new WorkflowSaveDraftResult { WorkflowId = workflowId, DraftId = draftId };
    }

    public async Task<WorkflowPublishResult> PublishVersionAsync(WorkflowPublishRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var draft = await db.WorkflowDrafts.FindAsync(request.DraftId)
            ?? throw new InvalidOperationException("Draft not found.");

        var publishedVersions = await db.WorkflowVersions
            .Where(v => v.WorkflowId == draft.WorkflowId && v.State != WorkflowDefinitionState.Draft)
            .ToListAsync();
        var latestVersion = publishedVersions.OrderByDescending(v => v.Version).FirstOrDefault();

        Version newVersion;
        if (latestVersion != null)
            newVersion = request.IsMajor
                ? new Version(latestVersion.Version.Major + 1, 0)
                : new Version(latestVersion.Version.Major, latestVersion.Version.Minor + 1);
        else
            newVersion = request.IsMajor ? new Version(1, 0) : new Version(0, 1);

        var audiences = request.InitialAudiences
            ?? draft.Definition.Roles.ToDictionary(r => r.Id, _ => new RoleAudience());

        var version = new WorkflowVersion
        {
            Id = Guid.NewGuid(),
            WorkflowId = draft.WorkflowId,
            Version = newVersion,
            Name = draft.Name,
            Description = draft.Description,
            Definition = draft.Definition,
            State = WorkflowDefinitionState.Published,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = draft.CreatedBy,
            RoleAudiences = audiences
        };

        db.WorkflowVersions.Add(version);
        db.WorkflowDrafts.Remove(draft);
        await db.SaveChangesAsync();

        await _auditService.RecordAsync(
            category: "Admin",
            eventType: nameof(WorkflowAuditEventType.WorkflowPublished),
            actor: request.UserId,
            details: new { WorkflowName = version.Name, Version = version.Version.ToString(), VersionId = version.Id });

        return new WorkflowPublishResult
        {
            VersionId = version.Id,
            Definition = version.Definition,
            RoleAudiences = version.RoleAudiences
        };
    }

    public async Task<WorkflowDesignerLoadResult> DeployVersionAsync(Guid versionId, string? userId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var version = await db.WorkflowVersions.FindAsync(versionId)
            ?? throw new InvalidOperationException("Version not found.");

        var previouslyDeployed = await db.WorkflowVersions
            .Where(v => v.WorkflowId == version.WorkflowId && v.State == WorkflowDefinitionState.Deployed)
            .ToListAsync();
        foreach (var v in previouslyDeployed)
            v.State = WorkflowDefinitionState.Published;

        version.State = WorkflowDefinitionState.Deployed;
        await db.SaveChangesAsync();

        await _auditService.RecordAsync(
            category: "Admin",
            eventType: nameof(WorkflowAuditEventType.WorkflowDeployed),
            actor: userId,
            details: new { WorkflowName = version.Name, Version = version.Version.ToString(), VersionId = versionId });

        return new WorkflowDesignerLoadResult
        {
            WorkflowId = version.WorkflowId,
            Name = version.Name,
            Description = version.Description,
            Definition = version.Definition,
            VersionId = version.Id,
            VersionRoleAudiences = version.RoleAudiences
        };
    }

    public async Task SaveRoleAudiencesAsync(Guid versionId, Dictionary<Guid, RoleAudience> audiences)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var version = await db.WorkflowVersions.FindAsync(versionId);
        if (version == null) return;
        version.RoleAudiences = audiences;
        await db.SaveChangesAsync();
    }

    public async Task<Dictionary<Guid, RoleAudience>?> GetVersionAudiencesAsync(Guid versionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var version = await db.WorkflowVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId);
        return version?.RoleAudiences;
    }

    public async Task<WorkflowSaveDraftResult> CreateDraftFromVersionAsync(Guid versionId, string? userId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var source = await db.WorkflowVersions.FindAsync(versionId)
            ?? throw new InvalidOperationException("Version not found.");

        if (await db.WorkflowDrafts.AnyAsync(d => d.WorkflowId == source.WorkflowId))
            throw new InvalidOperationException("A draft already exists for this workflow.");

        var now = DateTime.UtcNow;
        var draft = new WorkflowDraft
        {
            Id = Guid.NewGuid(),
            WorkflowId = source.WorkflowId,
            Name = source.Name,
            Description = source.Description,
            Definition = source.Definition,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId ?? "Unknown"
        };
        db.WorkflowDrafts.Add(draft);
        await db.SaveChangesAsync();

        return new WorkflowSaveDraftResult { WorkflowId = source.WorkflowId, DraftId = draft.Id };
    }

    public async Task<WorkflowDesignerLoadResult?> DiscardDraftAsync(Guid draftId, Guid workflowId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var draft = await db.WorkflowDrafts.FindAsync(draftId);
        if (draft != null)
            db.WorkflowDrafts.Remove(draft);
        await db.SaveChangesAsync();

        var version = await db.WorkflowVersions.AsNoTracking()
            .Where(v => v.WorkflowId == workflowId && v.State == WorkflowDefinitionState.Deployed)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync()
            ?? await db.WorkflowVersions.AsNoTracking()
                .Where(v => v.WorkflowId == workflowId)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync();

        if (version == null) return null;

        return new WorkflowDesignerLoadResult
        {
            WorkflowId = workflowId,
            Name = version.Name,
            Description = version.Description,
            Definition = version.Definition,
            VersionId = version.Id,
            VersionRoleAudiences = version.RoleAudiences
        };
    }

    public async Task<WorkflowVersionTimeline?> GetVersionTimelineAsync(Guid workflowId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var draft = await db.WorkflowDrafts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.WorkflowId == workflowId);

        var versions = await db.WorkflowVersions.AsNoTracking()
            .Where(v => v.WorkflowId == workflowId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();

        return new WorkflowVersionTimeline
        {
            Draft = draft == null ? null : new WorkflowDraftEntry
            {
                Id = draft.Id,
                CreatedBy = draft.CreatedBy,
                CreatedAt = draft.CreatedAt,
                UpdatedAt = draft.UpdatedAt
            },
            Versions = versions.Select(v => new WorkflowVersionEntry
            {
                Id = v.Id,
                VersionLabel = v.Version.ToString(),
                VersionMajor = v.Version.Major,
                VersionMinor = v.Version.Minor,
                Name = v.Name,
                CreatedBy = v.CreatedBy,
                CreatedAt = v.CreatedAt,
                IsDeployed = v.State == WorkflowDefinitionState.Deployed,
                IsPublished = v.State == WorkflowDefinitionState.Published,
                Roles = v.Definition.Roles
            }).ToList()
        };
    }

    public async Task<WorkflowDiff?> GetDraftDiffAsync(Guid workflowId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var draft = await db.WorkflowDrafts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.WorkflowId == workflowId);
        if (draft == null) return null;

        var latestVersion = await db.WorkflowVersions.AsNoTracking()
            .Where(v => v.WorkflowId == workflowId)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestVersion == null) return null;

        return WorkflowDiff.Compare(latestVersion.Definition, draft.Definition);
    }
}
