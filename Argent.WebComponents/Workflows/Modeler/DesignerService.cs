using Argent.Core.Workflows;
using Argent.Core.Workflows.Designer;
using Argent.Core.Enums;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler.Routing;
using Argent.WebComponents.Workflows.Modeler.Validation;
using System.Text.Json;

namespace Argent.WebComponents.Workflows.Modeler;

public class DesignerService(
    IWorkflowDesignerStore _store,
    IWorkflowNodeRegistry _registry)
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

    public Guid? LoadedDraftId { get; private set; }
    public Guid? LoadedVersionId { get; private set; }
    public bool IsReadOnlyVersion => LoadedVersionId != null;

    public Dictionary<Guid, RoleAudience> LoadedVersionRoleAudiences { get; private set; } = [];

    public WorkflowValidator Validator { get; } = new();

    public ValidationResult? ValidationResult { get; private set; }
    public WorkflowDefinition? CompiledDefinition { get; private set; }
    public string? CompiledJson { get; private set; }

    public bool HasUnsavedChanges { get; set; }
    public event Action? OnChange;

    /// <summary>Set by the hosting component on initialization from AuthenticationStateProvider.</summary>
    public string UserId { get; set; } = "Unknown";

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
        var result = await _store.LoadWorkflowAsync(workflowId);
        if (result == null) return;

        ResetCanvas();
        CurrentWorkflowId = result.WorkflowId;
        CurrentWorkflowName = result.Name;
        CurrentWorkflowDescription = result.Description;
        LoadedDraftId = result.DraftId;
        LoadedVersionId = result.VersionId;
        LoadedVersionRoleAudiences = result.VersionRoleAudiences;

        if (result.Definition != null)
            LoadDefinition(result.Definition);
    }

    public async Task LoadVersionAsync(Guid versionId)
    {
        var result = await _store.LoadVersionAsync(versionId);
        if (result == null) return;

        ResetCanvas();
        CurrentWorkflowId = result.WorkflowId;
        CurrentWorkflowName = result.Name;
        CurrentWorkflowDescription = result.Description;
        LoadedDraftId = result.DraftId;
        LoadedVersionId = result.VersionId;
        LoadedVersionRoleAudiences = result.VersionRoleAudiences;

        if (result.Definition != null)
            LoadDefinition(result.Definition);
    }

    public void Compile()
    {
        var now = DateTime.UtcNow;

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
                CreatedAt = now,
                CreatedBy = UserId,
                UpdatedAt = now,
                UpdatedBy = UserId,
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

    public DesignerLane? FindContainingLane(double cx, double cy)
        => Lanes.FirstOrDefault(l =>
            cx >= l.X && cx <= l.X + l.Width &&
            cy >= l.Y && cy <= l.Y + l.Height);

    public static (double x, double y) ClampToLane(DesignerLane lane, double x, double y, double w, double h)
        => (Math.Clamp(x, lane.X, lane.X + lane.Width - w),
            Math.Clamp(y, lane.Y, lane.Y + lane.Height - h));

    public DesignerPool AddPool(double x = 100, double y = 100, double width = 840, double height = 400)
    {
        var pool = new Pool { Label = "Pool", IsHorizontal = true };
        var dp = new DesignerPool { Data = pool, X = x, Y = y, Width = width, Height = height };
        Pools.Add(dp);

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
        var remaining = Lanes.Where(l => l.Pool == lane.Pool && l != lane)
            .OrderBy(l => l.Data.Order).ToList();
        for (var i = 0; i < remaining.Count; i++)
            remaining[i].Data.Order = i;

        Lanes.Remove(lane);
        RedistributeLanes(lane.Pool);

        if (SelectedLane == lane) SelectedLane = null;
        MarkDirty();
    }

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

    public ProcessRole AddRole(string name = "New Role")
    {
        var role = new ProcessRole { Name = name };
        Roles.Add(role);
        MarkDirty();
        return role;
    }

    public void RemoveRole(ProcessRole role)
    {
        foreach (var lane in Lanes.Where(l => l.Data.RoleId == role.Id))
            lane.Data.RoleId = null;
        Roles.Remove(role);
        MarkDirty();
    }

    public async Task SaveRoleAudiencesAsync(Dictionary<Guid, RoleAudience> audiences, CancellationToken ct = default)
    {
        if (!LoadedVersionId.HasValue) return;
        await _store.SaveRoleAudiencesAsync(LoadedVersionId.Value, audiences);
        LoadedVersionRoleAudiences = audiences;
        Notify();
    }

    public async Task SaveDraftAsync()
    {
        if (LoadedVersionId.HasValue || CompiledDefinition == null) return;

        var result = await _store.SaveDraftAsync(new WorkflowSaveDraftRequest
        {
            WorkflowId = CurrentWorkflowId,
            ExistingDraftId = LoadedDraftId,
            Name = CurrentWorkflowName,
            Description = CurrentWorkflowDescription,
            Definition = CompiledDefinition,
            UserId = UserId
        });

        CurrentWorkflowId = result.WorkflowId;
        LoadedDraftId = result.DraftId;
        LoadedVersionId = null;
        HasUnsavedChanges = false;
    }

    public async Task PublishVersionAsync(bool isMajor, Dictionary<Guid, RoleAudience>? initialAudiences = null)
    {
        if (!LoadedDraftId.HasValue) return;

        var result = await _store.PublishVersionAsync(new WorkflowPublishRequest
        {
            DraftId = LoadedDraftId.Value,
            IsMajor = isMajor,
            InitialAudiences = initialAudiences,
            UserId = UserId
        });

        LoadedVersionId = result.VersionId;
        LoadedDraftId = null;
        LoadedVersionRoleAudiences = result.RoleAudiences;
        LoadDefinition(result.Definition);
        HasUnsavedChanges = false;
    }

    public async Task DeployVersionAsync(Guid versionId)
    {
        var result = await _store.DeployVersionAsync(versionId, UserId);

        CurrentWorkflowId = result.WorkflowId;
        CurrentWorkflowName = result.Name;
        CurrentWorkflowDescription = result.Description;
        LoadedVersionId = result.VersionId;
        LoadedDraftId = null;
        LoadedVersionRoleAudiences = result.VersionRoleAudiences;

        if (result.Definition != null)
            LoadDefinition(result.Definition);

        HasUnsavedChanges = false;
    }

    public async Task CreateDraftFromVersionAsync(Guid versionId)
    {
        var result = await _store.CreateDraftFromVersionAsync(versionId, UserId);
        await LoadWorkflowAsync(result.WorkflowId);
    }

    public async Task DiscardDraftAsync()
    {
        if (!LoadedDraftId.HasValue || !CurrentWorkflowId.HasValue) return;

        var result = await _store.DiscardDraftAsync(LoadedDraftId.Value, CurrentWorkflowId.Value);

        ResetCanvas();
        LoadedDraftId = null;

        if (result != null)
        {
            CurrentWorkflowId = result.WorkflowId;
            CurrentWorkflowName = result.Name;
            CurrentWorkflowDescription = result.Description;
            LoadedVersionId = result.VersionId;
            LoadedVersionRoleAudiences = result.VersionRoleAudiences;

            if (result.Definition != null)
                LoadDefinition(result.Definition);
        }
        else
        {
            LoadedVersionId = null;
            LoadedVersionRoleAudiences = [];
        }

        HasUnsavedChanges = false;
        Notify();
    }

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
