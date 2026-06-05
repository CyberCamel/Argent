using Argent.Models.Workflows;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Argent.Infrastructure.Data;
using Argent.Runtime.Workflows.Modeling.Validation;

namespace Argent.Runtime.Workflows.Modeling;

public class DesignerService(IHttpContextAccessor _httpContextAccessor, ApplicationDbContext _dbContext)
{
    public DesignerSession Session { get; } = new();
    public List<DesignerNode> Nodes { get; } = [];
    public List<DesignerConnection> Connections { get; } = [];
    public ConnectionDraft? ActiveDraft { get; set; }
    public DesignerNode? PendingNewNode { get; set; }
    public DesignerNode? SelectedNode { get; set; }
    public DesignerConnection? SelectedConnection { get; set; }

    public WorkflowValidator Validator { get; } = new();

    // --- Clean State Properties ---
    public ValidationResult? ValidationResult { get; private set; }
    public WorkflowDefinition? CompiledDefinition { get; private set; }
    public string? CompiledJson { get; private set; } // Ready to read at any time by any tab

    public event Action? OnChange;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Compile()
    {
        // Grab the user safely from the current HttpContext
        var currentUser = _httpContextAccessor.HttpContext?.User;
        var createdBy = currentUser?.Identity?.Name ?? "Unknown";

        var def = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata()
            {
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            },
            Connections = [.. Connections.Select(c => c.EngineConnection)],
            Nodes = [.. Nodes.Select(n => n.NodeData)]
        };
        
        // Update all related state at the exact same moment
        CompiledDefinition = def;
        ValidationResult = Validator.Validate(def);
        CompiledJson = JsonSerializer.Serialize(def, JsonOptions);
        
        // Notify the entire UI layer that a mutation occurred
        Notify();
    }

    public void PersistDefinition()
    {
        if (CompiledDefinition == null) return;

        Workflow wf = new()
        {
            Definition = CompiledDefinition,
            Description = "A little workflow",
            Id = Guid.NewGuid(),
            Name = "My Workflow"
        };
        
        _dbContext.WorkflowDefinitions.Add(wf);
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