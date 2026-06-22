using Argent.Contracts.Workflows;
using Argent.Contracts.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using Argent.Models.Enums;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
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
    public List<DesignerPool> Pools { get; } = [];
    public List<DesignerLane> Lanes { get; } = [];
    public List<ProcessRole> Roles { get; } = [];

    public DesignerNode? SelectedNode { get; set; }
    public DesignerConnection? SelectedConnection { get; set; }
    public DesignerPool? SelectedPool { get; set; }
    public DesignerLane? SelectedLane { get; set; }

    public Guid? CurrentWorkflowId { get; set; }
    public string CurrentWorkflowName { get; set; } = "New Workflow";
    public string CurrentWorkflowDescription { get; set; } = "";

    // --- Draft / Version tracking ---
    public Guid? LoadedDraftId { get; private set; }
    public Guid? LoadedVersionId { get; private set; }
    public bool IsReadOnlyVersion => LoadedVersionId != null;

    // Audiences for the currently loaded version (mutable regardless of read-only status).
    public Dictionary<Guid, RoleAudience> LoadedVersionRoleAudiences { get; private set; } = [];

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

    private void ResetCanvas()
    {
        Nodes.Clear();
        Connections.Clear();
        Pools.Clear();
        Lanes.Clear();
        Roles.Clear();
        SelectedNode = null;
        SelectedConnection = null;
        SelectedPool = null;
        SelectedLane = null;
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

        // Load pools and lanes
        foreach (var pool in def.Pools)
        {
            var poolLayout = def.Layouts?.GetValueOrDefault(pool.Id);
            var dp = new DesignerPool
            {
                Data = pool,
                X = poolLayout?.X ?? 100,
                Y = poolLayout?.Y ?? 100,
                Width = poolLayout?.Width ?? 800,
                Height = poolLayout?.Height ?? 400
            };
            Pools.Add(dp);

            foreach (var lane in pool.Lanes)
            {
                var laneLayout = def.Layouts?.GetValueOrDefault(lane.Id);
                var dl = new DesignerLane
                {
                    Data = lane,
                    Pool = dp,
                    X = laneLayout?.X ?? dp.X + 40,
                    Y = laneLayout?.Y ?? dp.Y,
                    Width = laneLayout?.Width ?? dp.Width - 40,
                    Height = laneLayout?.Height ?? dp.Height / Math.Max(1, pool.Lanes.Count)
                };
                Lanes.Add(dl);
            }
        }

        Roles.AddRange(def.Roles);

        Notify();
        HasUnsavedChanges = false;
    }

    public async Task LoadWorkflowAsync(Guid workflowId)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        ResetCanvas();
        LoadedDraftId = null;
        LoadedVersionId = null;
        LoadedVersionRoleAudiences = [];

        var workflow = await dbContext.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowId);

        if (workflow == null) return;

        CurrentWorkflowId = workflow.Id;
        CurrentWorkflowName = workflow.Name;
        CurrentWorkflowDescription = workflow.Description;

        var draft = await dbContext.WorkflowDrafts
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.WorkflowId == workflowId);

        if (draft != null)
        {
            LoadDefinition(draft.Definition);
            LoadedDraftId = draft.Id;
            return;
        }

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
            LoadedVersionRoleAudiences = version.RoleAudiences;
        }
    }

    public async Task LoadVersionAsync(Guid versionId)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        ResetCanvas();
        LoadedDraftId = null;
        LoadedVersionId = null;
        LoadedVersionRoleAudiences = [];

        var version = await dbContext.WorkflowVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId);

        if (version == null) return;

        CurrentWorkflowId = version.WorkflowId;
        CurrentWorkflowName = version.Name;
        CurrentWorkflowDescription = version.Description;

        LoadDefinition(version.Definition);
        LoadedVersionId = version.Id;
        LoadedVersionRoleAudiences = version.RoleAudiences;
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

        // Serialize pool and lane positions into the layouts dict
        foreach (var dp in Pools)
        {
            layouts[dp.Data.Id] = new NodeLayout { X = dp.X, Y = dp.Y, Width = dp.Width, Height = dp.Height };
            dp.Data.Lanes = [.. Lanes
                .Where(dl => dl.Pool == dp)
                .OrderBy(dl => dl.Data.Order)
                .Select(dl => dl.Data)];
        }
        foreach (var dl in Lanes)
        {
            layouts[dl.Data.Id] = new NodeLayout { X = dl.X, Y = dl.Y, Width = dl.Width, Height = dl.Height };
        }

        var nodeLanes = ComputeNodeLanes();

        // Bake the lane role reference into UserActivity nodes
        foreach (var dn in Nodes)
        {
            if (dn.NodeData is UserActivity ua)
            {
                ua.LaneRoleId = nodeLanes.TryGetValue(ua.Id, out var laneId)
                    ? Lanes.FirstOrDefault(l => l.Data.Id == laneId)?.Data.RoleId
                    : null;
            }
        }

        var def = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata
            {
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userName,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = userName,
                State = WorkflowDefinitionState.Draft
            },
            Connections = [.. Connections.Select(c => c.EngineConnection)],
            Nodes = [.. Nodes.Select(n => n.NodeData)],
            Layouts = layouts,
            Pools = [.. Pools.Select(dp => dp.Data)],
            Roles = [.. Roles],
            NodeLanes = nodeLanes
        };

        CompiledDefinition = def;
        ValidationResult = Validator.Validate(def);
        CompiledJson = JsonSerializer.Serialize(def, JsonOptions);

        Notify();
    }

    // Returns a mapping from nodeId to the laneId of the lane whose bounds contain the node's center.
    private Dictionary<Guid, Guid> ComputeNodeLanes()
    {
        var result = new Dictionary<Guid, Guid>();
        foreach (var dn in Nodes)
        {
            var cx = dn.X + dn.Width / 2.0;
            var cy = dn.Y + dn.Height / 2.0;
            var lane = Lanes.FirstOrDefault(l =>
                cx >= l.X && cx <= l.X + l.Width &&
                cy >= l.Y && cy <= l.Y + l.Height);
            if (lane != null)
                result[dn.NodeData.Id] = lane.Data.Id;
        }
        return result;
    }

    // Returns the lane that fully contains the given center point, or null.
    public DesignerLane? FindContainingLane(double cx, double cy)
        => Lanes.FirstOrDefault(l =>
            cx >= l.X && cx <= l.X + l.Width &&
            cy >= l.Y && cy <= l.Y + l.Height);

    // Clamps a node's position so it stays within the given lane's bounds.
    public static (double x, double y) ClampToLane(DesignerLane lane, double x, double y, double w, double h)
        => (Math.Clamp(x, lane.X, lane.X + lane.Width - w),
            Math.Clamp(y, lane.Y, lane.Y + lane.Height - h));

    // ── Pool / Lane mutation ──────────────────────────────────────────────────

    public DesignerPool AddPool(double x = 100, double y = 100, double width = 840, double height = 400)
    {
        var pool = new Pool { Label = "Pool", IsHorizontal = true };
        var dp = new DesignerPool { Data = pool, X = x, Y = y, Width = width, Height = height };
        Pools.Add(dp);

        // Two default lanes splitting the initial pool height equally
        const double headerWidth = 40;
        double laneH = height / 2;
        for (int i = 0; i < 2; i++)
        {
            var lane = new Lane { Label = $"Lane {i + 1}", PoolId = pool.Id, Order = i };
            Lanes.Add(new DesignerLane
            {
                Data = lane,
                Pool = dp,
                X = x + headerWidth,
                Y = y + i * laneH,
                Width = width - headerWidth,
                Height = laneH
            });
        }

        MarkDirty();
        return dp;
    }

    public DesignerLane AddLane(DesignerPool pool, string label = "Lane", int order = -1)
    {
        if (order < 0) order = Lanes.Count(l => l.Pool == pool);

        const double defaultLaneHeight = 200;
        const double headerWidth = 40;

        var lane = new Lane { Label = label, PoolId = pool.Data.Id, Order = order };
        var dl = new DesignerLane
        {
            Data = lane,
            Pool = pool,
            X = pool.X + headerWidth,
            Y = pool.Y + pool.Height,
            Width = pool.Width - headerWidth,
            Height = defaultLaneHeight
        };
        Lanes.Add(dl);
        pool.Height += defaultLaneHeight;
        MarkDirty();
        return dl;
    }

    public void RemovePool(DesignerPool pool)
    {
        var poolLanes = Lanes.Where(l => l.Pool == pool).ToList();
        foreach (var lane in poolLanes)
            Lanes.Remove(lane);
        Pools.Remove(pool);

        if (SelectedPool == pool) SelectedPool = null;
        MarkDirty();
    }

    public void RemoveLane(DesignerLane lane)
    {
        // Re-order remaining lanes
        var remaining = Lanes.Where(l => l.Pool == lane.Pool && l != lane)
            .OrderBy(l => l.Data.Order).ToList();
        for (var i = 0; i < remaining.Count; i++)
            remaining[i].Data.Order = i;

        Lanes.Remove(lane);
        RedistributeLanes(lane.Pool);

        if (SelectedLane == lane) SelectedLane = null;
        MarkDirty();
    }

    // Evenly divides pool height among its lanes (called after add/remove lane operations).
    public void RedistributeLanes(DesignerPool pool)
    {
        const double headerWidth = 40;
        var poolLanes = Lanes.Where(l => l.Pool == pool).OrderBy(l => l.Data.Order).ToList();
        if (poolLanes.Count == 0) return;

        var laneHeight = pool.Height / poolLanes.Count;
        for (var i = 0; i < poolLanes.Count; i++)
        {
            poolLanes[i].X = pool.X + headerWidth;
            poolLanes[i].Y = pool.Y + i * laneHeight;
            poolLanes[i].Width = pool.Width - headerWidth;
            poolLanes[i].Height = laneHeight;
        }
    }

    // Moves a pool (and all its lanes) by the given delta; clamps nodes in lanes.
    public void MovePool(DesignerPool pool, double dx, double dy)
    {
        pool.X += dx;
        pool.Y += dy;
        foreach (var lane in Lanes.Where(l => l.Pool == pool))
        {
            lane.X += dx;
            lane.Y += dy;
        }
        MarkDirty();
    }

    // ── Role mutation ─────────────────────────────────────────────────────────

    public ProcessRole AddRole(string name = "New Role")
    {
        var role = new ProcessRole { Name = name };
        Roles.Add(role);
        MarkDirty();
        return role;
    }

    public void RemoveRole(ProcessRole role)
    {
        // Clear any lane references to this role
        foreach (var lane in Lanes.Where(l => l.Data.RoleId == role.Id))
            lane.Data.RoleId = null;
        Roles.Remove(role);
        MarkDirty();
    }

    // ── Audience (version-level, mutable post-publish) ────────────────────────

    public async Task SaveRoleAudiencesAsync(Dictionary<Guid, RoleAudience> audiences, CancellationToken ct = default)
    {
        if (!LoadedVersionId.HasValue) return;

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var version = await db.WorkflowVersions.FindAsync([LoadedVersionId.Value], ct);
        if (version == null) return;

        version.RoleAudiences = audiences;
        await db.SaveChangesAsync(ct);
        LoadedVersionRoleAudiences = audiences;
        Notify();
    }

    // ── Save / Publish / Deploy ───────────────────────────────────────────────

    public void SaveDraft()
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        if (LoadedVersionId.HasValue) return;
        if (CompiledDefinition == null) return;

        var currentUser = _httpContextAccessor.HttpContext?.User;
        var userName = currentUser?.Identity?.Name ?? "Unknown";
        var now = DateTime.UtcNow;

        CompiledDefinition.Metadata.UpdatedAt = now;
        CompiledDefinition.Metadata.UpdatedBy = userName;

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

    public async Task PublishVersionAsync(bool isMajor, Dictionary<Guid, RoleAudience>? initialAudiences = null)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        if (!LoadedDraftId.HasValue) return;

        var draft = dbContext.WorkflowDrafts.Find(LoadedDraftId.Value);
        if (draft == null) return;

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

        // Seed initial audiences: empty audience per role so the UI has entries to edit
        var audiences = initialAudiences ?? draft.Definition.Roles
            .ToDictionary(r => r.Id, _ => new RoleAudience());

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
            CreatedBy = draft.CreatedBy,
            RoleAudiences = audiences
        };

        dbContext.WorkflowVersions.Add(versionEntry);
        dbContext.WorkflowDrafts.Remove(draft);
        dbContext.SaveChanges();

        LoadedVersionId = versionEntry.Id;
        LoadedDraftId = null;
        LoadedVersionRoleAudiences = versionEntry.RoleAudiences;
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
        using var dbContext = _dbContextFactory.CreateDbContext();
        var version = dbContext.WorkflowVersions.Find(versionId);
        if (version == null || version.State != WorkflowDefinitionState.Published) return;

        var previouslyDeployed = dbContext.WorkflowVersions
            .Where(v => v.WorkflowId == version.WorkflowId && v.State == WorkflowDefinitionState.Deployed)
            .ToList();
        foreach (var v in previouslyDeployed)
            v.State = WorkflowDefinitionState.Published;

        version.State = WorkflowDefinitionState.Deployed;
        dbContext.SaveChanges();

        LoadedVersionId = versionId;
        LoadedDraftId = null;
        LoadedVersionRoleAudiences = version.RoleAudiences;
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
        using var dbContext = _dbContextFactory.CreateDbContext();
        var source = dbContext.WorkflowVersions.Find(versionId);
        if (source == null) return;

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

        CurrentWorkflowId = source.WorkflowId;
        CurrentWorkflowName = source.Name;
        CurrentWorkflowDescription = source.Description;
        LoadedDraftId = draft.Id;
        LoadedVersionId = null;
        LoadedVersionRoleAudiences = [];
        LoadDefinition(draft.Definition);
        HasUnsavedChanges = false;
    }

    public void DiscardDraft()
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        if (!LoadedDraftId.HasValue || !CurrentWorkflowId.HasValue) return;

        var draft = dbContext.WorkflowDrafts.Find(LoadedDraftId.Value);
        if (draft != null)
            dbContext.WorkflowDrafts.Remove(draft);
        dbContext.SaveChanges();

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
            LoadedVersionRoleAudiences = version.RoleAudiences;
            LoadDefinition(version.Definition);
        }
        else
        {
            ResetCanvas();
            LoadedVersionId = null;
            LoadedVersionRoleAudiences = [];
        }

        LoadedDraftId = null;
        HasUnsavedChanges = false;
        Notify();
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    public void Select(object? item)
    {
        SelectedNode?.IsSelected = false;
        SelectedPool?.IsSelected = false;
        SelectedLane?.IsSelected = false;
        SelectedNode = null;
        SelectedConnection = null;
        SelectedPool = null;
        SelectedLane = null;

        switch (item)
        {
            case DesignerNode node:
                SelectedNode = node;
                node.IsSelected = true;
                break;
            case DesignerConnection conn:
                SelectedConnection = conn;
                break;
            case DesignerPool pool:
                SelectedPool = pool;
                pool.IsSelected = true;
                break;
            case DesignerLane lane:
                SelectedLane = lane;
                lane.IsSelected = true;
                break;
        }

        Notify();
    }

    public void DeselectAll() => Select(null);

    public void Notify() => OnChange?.Invoke();
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
        else if (SelectedPool != null)
        {
            RemovePool(SelectedPool);
        }
        else if (SelectedLane != null)
        {
            RemoveLane(SelectedLane);
        }
    }
}
