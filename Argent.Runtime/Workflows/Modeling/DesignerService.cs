using Argent.Contracts.Workflows;
using Argent.Runtime.Workflows.Execution;
using Argent.Models.Enums;
using Argent.Models.Workflows;
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
    ApplicationDbContext _dbContext,
    IWorkflowNodeRegistry _registry)
{
    public DesignerSession Session { get; } = new();
    public List<DesignerNode> Nodes { get; } = [];
    public List<DesignerConnection> Connections { get; } = [];
    public ConnectionDraft? ActiveDraft { get; set; }
    public DesignerNode? PendingNewNode { get; set; }
    public DesignerNode? SelectedNode { get; set; }
    public DesignerConnection? SelectedConnection { get; set; }

    public Guid? CurrentWorkflowId { get; set; }
    public string CurrentWorkflowName { get; set; } = "New Workflow";
    public string CurrentWorkflowDescription { get; set; } = "";

    // --- Versioning ---
    public Guid? LoadedVersionId { get; private set; }
    public bool IsReadOnlyVersion { get; private set; }
    private int _latestVersionNumber;

    public WorkflowValidator Validator { get; } = new();

    // --- Clean State Properties ---
    public ValidationResult? ValidationResult { get; private set; }
    public WorkflowDefinition? CompiledDefinition { get; private set; }
    public string? CompiledJson { get; private set; }

    // --- Execution State ---
    public bool IsExecuting { get; set; }
    public HashSet<Guid> ActiveTokenNodeIds { get; } = [];
    public HashSet<Guid> VisitedNodeIds { get; } = [];
    public event Action? OnChange;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void SetWorkflowMetadata(string name, string description)
    {
        CurrentWorkflowName = name;
        CurrentWorkflowDescription = description;
    }

    public void LoadDefinition(WorkflowDefinition def)
    {
        Nodes.Clear();
        Connections.Clear();
        CompiledDefinition = null;
        ValidationResult = null;
        CompiledJson = null;
        _latestVersionNumber = 0;
        LoadedVersionId = null;
        IsReadOnlyVersion = false;

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
    }

    public async Task LoadWorkflowAsync(Guid workflowId)
    {
        var workflow = await _dbContext.WorkflowDefinitions.FindAsync(workflowId);
        if (workflow == null) return;

        CurrentWorkflowId = workflow.Id;
        CurrentWorkflowName = workflow.Name;
        CurrentWorkflowDescription = workflow.Description;
        IsReadOnlyVersion = false;

        // Load the latest version from WorkflowVersions, or fall back to Workflow.Definition
        var latestVersion = await _dbContext.WorkflowVersions
            .Where(v => v.WorkflowId == workflowId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync();

        if (latestVersion != null)
        {
            LoadedVersionId = latestVersion.Id;
            _latestVersionNumber = latestVersion.VersionNumber;
            LoadDefinition(latestVersion.Definition);
        }
        else if (workflow.Definition != null)
        {
            LoadedVersionId = null;
            _latestVersionNumber = 0;
            LoadDefinition(workflow.Definition);
        }
    }

    public async Task LoadVersionAsync(Guid versionId)
    {
        var version = await _dbContext.WorkflowVersions.FindAsync(versionId);
        if (version == null) return;

        CurrentWorkflowId = version.WorkflowId;
        CurrentWorkflowName = version.Name;
        CurrentWorkflowDescription = version.Description;
        LoadedVersionId = version.Id;
        _latestVersionNumber = version.VersionNumber;

        var latest = await _dbContext.WorkflowVersions
            .Where(v => v.WorkflowId == version.WorkflowId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => v.VersionNumber)
            .FirstOrDefaultAsync();

        IsReadOnlyVersion = version.VersionNumber < latest;
        LoadDefinition(version.Definition);
    }

    public async Task<List<WorkflowVersion>> GetVersionsAsync(Guid workflowId)
    {
        return await _dbContext.WorkflowVersions
            .Where(v => v.WorkflowId == workflowId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();
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
                CreatedAt = CurrentWorkflowId.HasValue ? DateTime.UtcNow : DateTime.UtcNow,
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

    public void PersistDefinition()
    {
        if (CompiledDefinition == null) return;

        var currentUser = _httpContextAccessor.HttpContext?.User;
        var userName = currentUser?.Identity?.Name ?? "Unknown";

        CompiledDefinition.Metadata.UpdatedAt = DateTime.UtcNow;
        CompiledDefinition.Metadata.UpdatedBy = userName;

        // Create or update the Workflow entity
        if (!CurrentWorkflowId.HasValue)
        {
            var wf = new Workflow
            {
                Id = Guid.NewGuid(),
                Name = CurrentWorkflowName,
                Description = CurrentWorkflowDescription,
                Definition = CompiledDefinition
            };
            _dbContext.WorkflowDefinitions.Add(wf);
            CurrentWorkflowId = wf.Id;
        }
        else
        {
            var existing = _dbContext.WorkflowDefinitions.Find(CurrentWorkflowId.Value);
            if (existing != null)
            {
                existing.Name = CurrentWorkflowName;
                existing.Description = CurrentWorkflowDescription;
                existing.Definition = CompiledDefinition;
            }
        }

        // Create a new version entry
        var versionNumber = _latestVersionNumber + 1;
        var version = new WorkflowVersion
        {
            Id = Guid.NewGuid(),
            WorkflowId = CurrentWorkflowId.Value,
            VersionNumber = versionNumber,
            Name = CurrentWorkflowName,
            Description = CurrentWorkflowDescription,
            Definition = CompiledDefinition,
            State = WorkflowDefinitionState.Published,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userName
        };
        _dbContext.WorkflowVersions.Add(version);
        LoadedVersionId = version.Id;
        _latestVersionNumber = versionNumber;
        IsReadOnlyVersion = false;

        _dbContext.SaveChanges();
    }

    public void Notify() => OnChange?.Invoke();

    public void DeleteSelected()
    {
        if (SelectedNode != null)
        {
            Connections.RemoveAll(c => c.Source == SelectedNode || c.Target == SelectedNode);
            Nodes.Remove(SelectedNode);
            SelectedNode = null;
        }
        else if (SelectedConnection != null)
        {
            Connections.Remove(SelectedConnection);
            SelectedConnection = null;
        }
        Notify();
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
