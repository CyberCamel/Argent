using Argent.Contracts.Workflows;
using Argent.Contracts.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using Argent.Models.Enums;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Auditing;
using Argent.Models.Workflows.Modeler;
using Argent.Runtime.Workflows.Modeling.Routing;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Argent.Infrastructure.Data;
using Argent.Runtime.Workflows.Modeling.Validation;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Workflows.Modeling;

public class DesignerService(
    IHttpContextAccessor _httpContextAccessor,
    IDbContextFactory<ArgentDbContext> _dbContextFactory,
    IWorkflowNodeRegistry _registry,
    IAuditService _auditService)
{
    public List<DesignerNode> Nodes { get; } = [];
    public List<DesignerConnection> Connections { get; } = [];
    public DesignerNode? SelectedNode { get; set; }
    public DesignerConnection? SelectedConnection { get; set; }

    public Guid? CurrentWorkflowId { get; set; }
    public string CurrentWorkflowName { get; set; } = "New Workflow";
    public string CurrentWorkflowDescription { get; set; } = "";

    // --- Draft / Version tracking ---
    public Guid? LoadedDraftId { get; private set; }
    public Guid? LoadedVersionId { get; private set; }
    public bool IsReadOnlyVersion => LoadedVersionId != null;

    public WorkflowValidator Validator { get; } = new();

    // --- Clean State Properties ---
    public ValidationResult? ValidationResult { get; private set; }
    public WorkflowDefinition? CompiledDefinition { get; private set; }
    public string? CompiledJson { get; private set; }

    public bool HasUnsavedChanges { get; set; }
    public event Action? OnChange;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Clears the canvas (nodes, connections, selection, compiled output) but keeps
    /// workflow identity and draft/version tracking intact.
    /// </summary>
    private void ResetCanvas()
    {
        Nodes.Clear();
        Connections.Clear();
        SelectedNode = null;
        SelectedConnection = null;
        CompiledDefinition = null;
        ValidationResult = null;
        CompiledJson = null;
    }

    public void LoadDefinition(WorkflowDefinition def)
    {
        ResetCanvas();

        var nodeMap = new Dictionary<Guid, DesignerNode>();
        var metadataCache = _registry.GetRegisteredTypes().ToList();

        foreach (var nodeData in def.Nodes)
        {
            var nodeType = nodeData.GetType();
            var meta = metadataCache.FirstOrDefault(m => m.NodeType == nodeType);
            var layout = def.Layouts?.GetValueOrDefault(nodeData.Id);

            var designerNode = new DesignerNode
            {
                NodeData = nodeData,
                Title = meta?.DisplayName ?? nodeData.Name ?? "Unknown",
                Description = meta?.Description ?? "",
                Icon = meta?.Icon ?? "question_mark",
                CssClass = meta?.CssClass ?? "",
                Shape = meta?.Shape ?? NodeShape.Rectangle,
                X = layout?.X ?? 100 + (Nodes.Count % 5) * 220,
                Y = layout?.Y ?? 100 + (Nodes.Count / 5) * 160,
                Width = layout?.Width ?? meta?.DefaultWidth ?? 160,
                Height = layout?.Height ?? meta?.DefaultHeight ?? 80
            };

            Nodes.Add(designerNode);
            nodeMap[nodeData.Id] = designerNode;
        }

        foreach (var conn in def.Connections)
        {
            if (nodeMap.TryGetValue(conn.From.Id, out var source) &&
                nodeMap.TryGetValue(conn.To.Id, out var target))
            {
                conn.From = source.NodeData;
                conn.To = target.NodeData;

                var (srcDir, tgtDir) = RoutingService.GetBestDirections(source, target);
                var dc = new DesignerConnection
                {
                    EngineConnection = conn,
                    Source = source,
                    Target = target,
                    SourceDir = srcDir,
                    TargetDir = tgtDir
                };
                dc.Waypoints = RoutingService.AutoRoute(dc.Source, dc.SourceDir, dc.Target, dc.TargetDir);
                Connections.Add(dc);
            }
        }

        Notify();
        HasUnsavedChanges = false;
    }

    public async Task LoadWorkflowAsync(Guid workflowId)
    {
        await using var dbContext = await  _dbContextFactory.CreateDbContextAsync();
        ResetCanvas();
        LoadedDraftId = null;
        LoadedVersionId = null;

        var workflow = await dbContext.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowId);

        if (workflow == null) return;

        CurrentWorkflowId = workflow.Id;
        CurrentWorkflowName = workflow.Name;
        CurrentWorkflowDescription = workflow.Description;

        // Prefer loading the draft (editable)
        var draft = await dbContext.WorkflowDrafts
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.WorkflowId == workflowId);

        if (draft != null)
        {
            LoadDefinition(draft.Definition);
            LoadedDraftId = draft.Id;
            return;
        }

        // Fallback to deployed version, then latest
        var version = await dbContext.WorkflowVersions
            .AsNoTracking()
            .Where(v => v.WorkflowId == workflowId && v.State == WorkflowDefinitionState.Deployed)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync()
            ?? await dbContext.WorkflowVersions
                .AsNoTracking()
                .Where(v => v.WorkflowId == workflowId)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync();

        if (version != null)
        {
            LoadDefinition(version.Definition);
            LoadedVersionId = version.Id;
        }
    }

    public async Task LoadVersionAsync(Guid versionId)
    {
        await using var dbContext = await  _dbContextFactory.CreateDbContextAsync();
        ResetCanvas();
        LoadedDraftId = null;
        LoadedVersionId = null;

        var version = await dbContext.WorkflowVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId);

        if (version == null) return;

        CurrentWorkflowId = version.WorkflowId;
        CurrentWorkflowName = version.Name;
        CurrentWorkflowDescription = version.Description;

        LoadDefinition(version.Definition);
        LoadedVersionId = version.Id;
    }

    public void Compile()
    {
        var currentUser = _httpContextAccessor.HttpContext?.User;
        var userName = currentUser?.Identity?.Name ?? "Unknown";

        var layouts = new Dictionary<Guid, NodeLayout>();
        foreach (var node in Nodes)
        {
            layouts[node.NodeData.Id] = new NodeLayout
            {
                X = node.X,
                Y = node.Y,
                Width = node.Width,
                Height = node.Height
            };
        }

        var def = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata()
            {
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userName,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = userName,
                State = WorkflowDefinitionState.Draft
            },
            Connections = [.. Connections.Select(c => c.EngineConnection)],
            Nodes = [.. Nodes.Select(n => n.NodeData)],
            Layouts = layouts
        };

        CompiledDefinition = def;
        ValidationResult = Validator.Validate(def);
        CompiledJson = JsonSerializer.Serialize(def, JsonOptions);

        Notify();
    }

    public void SaveDraft()
    {
        using var dbContext =  _dbContextFactory.CreateDbContext();
        if (LoadedVersionId.HasValue) return;
        if (CompiledDefinition == null) return;

        var currentUser = _httpContextAccessor.HttpContext?.User;
        var userName = currentUser?.Identity?.Name ?? "Unknown";
        var now = DateTime.UtcNow;

        CompiledDefinition.Metadata.UpdatedAt = now;
        CompiledDefinition.Metadata.UpdatedBy = userName;

        // Create or update the Workflow entity (metadata only)
        if (!CurrentWorkflowId.HasValue)
        {
            var wf = new Workflow
            {
                Id = Guid.NewGuid(),
                Name = CurrentWorkflowName,
                Description = CurrentWorkflowDescription,
                CreatedOn = now,
                UpdatedOn = now,
                Tags = []
            };
            dbContext.Workflows.Add(wf);
            CurrentWorkflowId = wf.Id;
        }
        else
        {
            var existing = dbContext.Workflows.Find(CurrentWorkflowId.Value);
            if (existing != null)
            {
                existing.Name = CurrentWorkflowName;
                existing.Description = CurrentWorkflowDescription;
                existing.UpdatedOn = now;
            }
        }

        // Upsert the draft
        var draft = LoadedDraftId.HasValue
            ? dbContext.WorkflowDrafts.Find(LoadedDraftId.Value)
            : dbContext.WorkflowDrafts.FirstOrDefault(d => d.WorkflowId == CurrentWorkflowId.Value);

        if (draft != null)
        {
            draft.Definition = CompiledDefinition;
            draft.Name = CurrentWorkflowName;
            draft.Description = CurrentWorkflowDescription;
            draft.UpdatedAt = now;
            LoadedDraftId = draft.Id;
        }
        else
        {
            draft = new WorkflowDraft
            {
                Id = Guid.NewGuid(),
                WorkflowId = CurrentWorkflowId.Value,
                Name = CurrentWorkflowName,
                Description = CurrentWorkflowDescription,
                Definition = CompiledDefinition,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = userName
            };
            dbContext.WorkflowDrafts.Add(draft);
            LoadedDraftId = draft.Id;
        }

        LoadedVersionId = null;
        dbContext.SaveChanges();
        HasUnsavedChanges = false;
    }

    public async Task PublishVersionAsync(bool isMajor)
    {
        using var dbContext =  _dbContextFactory.CreateDbContext();
        if (!LoadedDraftId.HasValue) return;

        var draft = dbContext.WorkflowDrafts.Find(LoadedDraftId.Value);
        if (draft == null) return;

        // Find the highest published/deployed version to compute the bump.
        // System.Version doesn't translate to SQL, so order client-side.
        var latestVersion = dbContext.WorkflowVersions
            .Where(v => v.WorkflowId == draft.WorkflowId && v.State != WorkflowDefinitionState.Draft)
            .AsEnumerable()
            .OrderByDescending(v => v.Version)
            .FirstOrDefault();

        Version newVersion;
        if (latestVersion != null)
            newVersion = isMajor
                ? new Version(latestVersion.Version.Major + 1, 0)
                : new Version(latestVersion.Version.Major, latestVersion.Version.Minor + 1);
        else
            newVersion = isMajor ? new Version(1, 0) : new Version(0, 1);

        var versionEntry = new WorkflowVersion
        {
            Id = Guid.NewGuid(),
            WorkflowId = draft.WorkflowId,
            Version = newVersion,
            Name = draft.Name,
            Description = draft.Description,
            Definition = draft.Definition,
            State = WorkflowDefinitionState.Published,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = draft.CreatedBy
        };

        dbContext.WorkflowVersions.Add(versionEntry);
        dbContext.WorkflowDrafts.Remove(draft);
        dbContext.SaveChanges();

        // Now viewing the published version (read-only)
        LoadedVersionId = versionEntry.Id;
        LoadedDraftId = null;
        LoadDefinition(versionEntry.Definition);
        HasUnsavedChanges = false;

        await _auditService.RecordAsync(
            category: "Admin",
            eventType: nameof(WorkflowAuditEventType.WorkflowPublished),
            actor: _httpContextAccessor.HttpContext?.User?.Identity?.Name,
            details: new { WorkflowName = versionEntry.Name, Version = versionEntry.Version.ToString(), VersionId = versionEntry.Id });
    }

    public async Task DeployVersionAsync(Guid versionId)
    {
        using var dbContext =  _dbContextFactory.CreateDbContext();
        var version = dbContext.WorkflowVersions.Find(versionId);
        if (version == null || version.State != WorkflowDefinitionState.Published) return;

        // Un-deploy any previously deployed version for this workflow
        var previouslyDeployed = dbContext.WorkflowVersions
            .Where(v => v.WorkflowId == version.WorkflowId && v.State == WorkflowDefinitionState.Deployed)
            .ToList();
        foreach (var v in previouslyDeployed)
        {
            v.State = WorkflowDefinitionState.Published;
        }

        version.State = WorkflowDefinitionState.Deployed;
        dbContext.SaveChanges();

        // Switch the modeler to view this deployed version (read-only)
        LoadedVersionId = versionId;
        LoadedDraftId = null;
        LoadDefinition(version.Definition);
        HasUnsavedChanges = false;

        await _auditService.RecordAsync(
            category: "Admin",
            eventType: nameof(WorkflowAuditEventType.WorkflowDeployed),
            actor: _httpContextAccessor.HttpContext?.User?.Identity?.Name,
            details: new { WorkflowName = version.Name, Version = version.Version.ToString(), VersionId = versionId });
    }

    public void CreateDraftFromVersion(Guid versionId)
    {
        using var dbContext =  _dbContextFactory.CreateDbContext();
        var source = dbContext.WorkflowVersions.Find(versionId);
        if (source == null) return;

        // Don't create if a draft already exists — discard it first
        if (dbContext.WorkflowDrafts.Any(d => d.WorkflowId == source.WorkflowId))
            return;

        var currentUser = _httpContextAccessor.HttpContext?.User;
        var userName = currentUser?.Identity?.Name ?? "Unknown";
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
            CreatedBy = userName
        };
        dbContext.WorkflowDrafts.Add(draft);
        dbContext.SaveChanges();

        // Load the new draft into the editor
        CurrentWorkflowId = source.WorkflowId;
        CurrentWorkflowName = source.Name;
        CurrentWorkflowDescription = source.Description;
        LoadedDraftId = draft.Id;
        LoadedVersionId = null;
        LoadDefinition(draft.Definition);
        HasUnsavedChanges = false;
    }

    public void DiscardDraft()
    {
        using var dbContext =  _dbContextFactory.CreateDbContext();
        if (!LoadedDraftId.HasValue || !CurrentWorkflowId.HasValue) return;

        var draft = dbContext.WorkflowDrafts.Find(LoadedDraftId.Value);
        if (draft != null)
            dbContext.WorkflowDrafts.Remove(draft);
        dbContext.SaveChanges();

        // Fallback to the latest deployed/published version
        var version = dbContext.WorkflowVersions
                          .Where(v => v.WorkflowId == CurrentWorkflowId.Value && v.State == WorkflowDefinitionState.Deployed)
                          .OrderByDescending(v => v.CreatedAt)
                          .FirstOrDefault()
                      ?? dbContext.WorkflowVersions
                          .Where(v => v.WorkflowId == CurrentWorkflowId.Value)
                          .OrderByDescending(v => v.CreatedAt)
                          .FirstOrDefault();

        if (version != null)
        {
            LoadedVersionId = version.Id;
            LoadDefinition(version.Definition);
        }
        else
        {
            ResetCanvas();
            LoadedVersionId = null;
        }

        LoadedDraftId = null;
        HasUnsavedChanges = false;
        Notify();
    }

    public void Notify()
    {
        OnChange?.Invoke();
    }
    public void MarkDirty() { HasUnsavedChanges = true; Notify(); }

    public void DeleteSelected()
    {
        if (SelectedNode != null)
        {
            Connections.RemoveAll(c => c.Source == SelectedNode || c.Target == SelectedNode);
            Nodes.Remove(SelectedNode);
            SelectedNode = null;
            MarkDirty();
        }
        else if (SelectedConnection != null)
        {
            Connections.Remove(SelectedConnection);
            SelectedConnection = null;
            MarkDirty();
        }
    }

    public void Select(object? item)
    {
        SelectedNode?.IsSelected = false;
        SelectedNode = null;
        SelectedConnection = null;

        if (item is DesignerNode node)
        {
            SelectedNode = node;
            node.IsSelected = true;
        }
        else if (item is DesignerConnection conn)
        {
            SelectedConnection = conn;
        }

        Notify();
    }

    public void DeselectAll() => Select(null);
}
