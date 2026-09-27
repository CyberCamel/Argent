using System.Reflection;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Designer;
using Argent.Core.Enums;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.BoundaryEvents;
using Argent.Core.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler.Interaction;
using Argent.WebComponents.Workflows.Modeler.Routing;
using Argent.WebComponents.Workflows.Modeler.Undo;
using Argent.WebComponents.Workflows.Modeler.Validation;
using System.Text.Json;

namespace Argent.WebComponents.Workflows.Modeler;

public enum SaveState
{
    Saved,
    Unsaved,
    Saving,
    Failed
}

public class DesignerService(
    IWorkflowDesignerStore _store,
    IWorkflowNodeRegistry _registry)
{
    public List<DesignerNode> Nodes { get; } = [];
    public List<DesignerConnection> Connections { get; } = [];
    public List<DesignerPool> Pools { get; } = [];
    public List<DesignerLane> Lanes { get; } = [];
    public List<ProcessRole> Roles { get; } = [];

    /// <summary>Every selected node. Dragging any of them moves the whole set.</summary>
    public HashSet<DesignerNode> SelectedNodes { get; } = [];

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

    public DesignerHistory History { get; } = new();

    /// <summary>Why the last autosave failed, so the toolbar can offer a retry.</summary>
    public string? LastSaveError { get; private set; }

    public SaveState SaveState
    {
        get
        {
            if (_saving) return SaveState.Saving;
            if (LastSaveError != null && HasUnsavedChanges) return SaveState.Failed;
            return HasUnsavedChanges ? SaveState.Unsaved : SaveState.Saved;
        }
    }

    private bool _saving;
    private int _autoSaveSuspensions;
    private DesignerSnapshot? _savedSnapshot;

    public bool CanAutoSave => _autoSaveSuspensions == 0 && !_saving;

    public void SuspendAutoSave() => _autoSaveSuspensions++;

    public void ResumeAutoSave()
    {
        if (_autoSaveSuspensions > 0) _autoSaveSuspensions--;
    }

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
        ClearSelectionFlags();
        SelectedNodes.Clear();
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
        History.Clear();

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
            if (!nodeMap.TryGetValue(conn.From.Id, out var source) ||
                !nodeMap.TryGetValue(conn.To.Id, out var target))
                continue;

            conn.From = source.NodeData;
            conn.To = target.NodeData;
            conn.Id = conn.Id == Guid.Empty ? Guid.NewGuid() : conn.Id;

            var dc = new DesignerConnection
            {
                EngineConnection = conn,
                Source = source,
                Target = target
            };

            // The stored route intent is reapplied here; definitions without one are
            // routed automatically exactly as before.
            ElasticRouter.Route(dc, Nodes);
            Connections.Add(dc);
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
        LastSaveError = null;
        _savedSnapshot = DesignerSnapshot.Capture(this);
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

        // Connections carry their own identity and route intent, so both survive a save,
        // a publish, a version view and a duplication without extra plumbing here.
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
            double cx = dn.X + dn.Width / 2.0;
            double cy = dn.Y + dn.Height / 2.0;
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

        _saving = true;
        LastSaveError = null;
        Notify();
        try
        {
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
            _savedSnapshot = DesignerSnapshot.Capture(this);
        }
        catch (Exception ex)
        {
            // The unsaved work stays in the model so the user can retry.
            LastSaveError = ex.Message;
            HasUnsavedChanges = true;
        }
        finally
        {
            _saving = false;
            Notify();
        }
    }

    /// <summary>Autosave, skipped while a gesture is open or while the canvas is read-only.</summary>
    public async Task<bool> TryAutoSaveAsync(bool readOnly)
    {
        if (readOnly) return false;
        if (CurrentWorkflowId == null) return false;   // never silently create a workflow
        if (!CanAutoSave) return false;
        if (!HasUnsavedChanges) return false;

        Compile();
        await SaveDraftAsync();
        return !HasUnsavedChanges;
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
        History.Clear();
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
        ClearSelectionFlags();
        SelectedNodes.Clear();
        SelectedNode = null;
        SelectedConnection = null;
        SelectedPool = null;
        SelectedLane = null;

        switch (item)
        {
            case DesignerNode node:
                SelectedNode = node;
                SelectedNodes.Add(node);
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

    public void SelectNodes(IEnumerable<DesignerNode> nodes, DesignerNode? active = null)
    {
        ClearSelectionFlags();
        SelectedNodes.Clear();
        SelectedConnection = null;
        SelectedPool = null;
        SelectedLane = null;

        foreach (var node in nodes)
        {
            SelectedNodes.Add(node);
            node.IsSelected = true;
        }

        SelectedNode = active ?? SelectedNodes.LastOrDefault();
        Notify();
    }

    /// <summary>Adds or removes a node from the selection, keeping the active node sensible.</summary>
    public void ToggleNodeSelection(DesignerNode node)
    {
        if (!SelectedNodes.Remove(node))
        {
            SelectedNodes.Add(node);
            node.IsSelected = true;
            SelectedNode = node;
        }
        else
        {
            node.IsSelected = false;
            if (ReferenceEquals(SelectedNode, node))
                SelectedNode = SelectedNodes.LastOrDefault();
        }

        SelectedConnection = null;
        SelectedPool = null;
        SelectedLane = null;
        Notify();
    }

    public void ClearSelectionFlags()
    {
        foreach (var node in SelectedNodes) node.IsSelected = false;
        if (SelectedPool != null) SelectedPool.IsSelected = false;
        if (SelectedLane != null) SelectedLane.IsSelected = false;
    }

    public void DeselectAll() => Select(null);

    public void Notify() => OnChange?.Invoke();
    public void MarkDirty() { HasUnsavedChanges = true; Notify(); }

    /// <summary>
    /// Applies property changes as one undoable step. Consecutive edits of the same
    /// property collapse into a single entry, so typing in a field is one undo.
    /// </summary>
    public void ApplyEdits(string description, params (object Target, string Property, object? Value)[] edits)
    {
        if (edits.Length == 0) return;

        var entries = new PropertyChangeCommand.Entry[edits.Length];
        for (int i = 0; i < edits.Length; i++)
        {
            var property = edits[i].Target.GetType().GetProperty(edits[i].Property)
                ?? throw new ArgumentException($"No property {edits[i].Property} on {edits[i].Target.GetType().Name}.");
            var oldValue = property.GetValue(edits[i].Target);
            entries[i] = new PropertyChangeCommand.Entry(edits[i].Target, property, oldValue, edits[i].Value);
            property.SetValue(edits[i].Target, edits[i].Value);
        }

        History.Push(new PropertyChangeCommand(description, entries));
        MarkDirty();
    }

    public void Undo()
    {
        var command = History.Undo();
        if (command == null) return;
        command.Undo(this);
        EvaluateDirtyState();
    }

    public void Redo()
    {
        var command = History.Redo();
        if (command == null) return;
        command.Redo(this);
        EvaluateDirtyState();
    }

    /// <summary>
    /// Undoing back to the state that was last saved clears the dirty flag, so a user who
    /// explores a change and takes it back does not have to save an unchanged draft.
    /// </summary>
    private void EvaluateDirtyState()
    {
        if (_savedSnapshot == null) return;
        var current = DesignerSnapshot.Capture(this);
        HasUnsavedChanges = _savedSnapshot.DiffersFrom(current);
        Notify();
    }

    /// <summary>Removes the selected nodes, connections, pool or lane as one undoable step.</summary>
    public void DeleteSelection()
    {
        if (SelectedNodes.Count > 0)
        {
            var doomed = new HashSet<DesignerNode>(SelectedNodes);
            // Boundary events attached to a deleted node go with it.
            bool added;
            do
            {
                added = false;
                foreach (var child in Nodes.Where(n =>
                             n.NodeData is BoundaryEvent boundary &&
                             doomed.Any(p => p.NodeData.Id == boundary.ParentNodeId)).ToList())
                {
                    if (doomed.Add(child)) added = true;
                }
            } while (added);

            Connections.RemoveAll(c => doomed.Contains(c.Source) || doomed.Contains(c.Target));
            Nodes.RemoveAll(doomed.Contains);
            DeselectAll();
            MarkDirty();
            return;
        }

        if (SelectedConnection != null)
        {
            Connections.Remove(SelectedConnection);
            SelectedConnection = null;
            MarkDirty();
            return;
        }

        if (SelectedPool != null)
        {
            RemovePool(SelectedPool);
            return;
        }

        if (SelectedLane != null)
        {
            RemoveLane(SelectedLane);
        }
    }
}
