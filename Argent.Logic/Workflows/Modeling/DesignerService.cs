using Argent.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Argent.Logic.Workflows.Modeling;

public class DesignerService
{
    public DesignerSession Session { get; } = new();
    public List<DesignerNode> Nodes { get; } = new();
    public List<DesignerConnection> Connections { get; } = new();
    public ConnectionDraft? ActiveDraft { get; set; }
    public DesignerNode? PendingNewNode { get; set; } // For toolbox dragging
    public DesignerNode? SelectedNode { get; set; }
    public DesignerConnection? SelectedConnection { get; set; }

    public WorkflowDefinition? CompiledDefintion { get; set; }

    public event Action? OnChange;

    private bool _doCompile;

    public void Compile()
    {
        var def = new WorkflowDefinition
        {
            Connections = Connections.Select(c => c.EngineConnection).ToList(),
            Nodes = [.. Nodes.Select(n => n.NodeData)]
        };
        CompiledDefintion = def;
        _doCompile = true;

    }

    public string? CompileToJson()
    {

        if (!_doCompile)
        {
            return null;
        }

        _doCompile = false;

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var export = new
        {
            Metadata = new { CompiledAt = DateTime.Now, Version = "1.0" },
            // We project the nodes into a flat structure to break the circularity
            Activities = Nodes.Select(n => new {
                Id = n.Id,
                Type = n.NodeData.GetType().Name,
                Title = n.Title,
                // Use reflection or a specific check to get the 'Code' or other properties
                Data = n.NodeData
            }),
            Connections = Connections.Select(c => new {
                SourceId = c.Source.Id,
                TargetId = c.Target.Id,
                // We only save the IDs, not the whole object, to stop the recursion
                Expression = c.EngineConnection.Expression
            })
        };

        return JsonSerializer.Serialize(export, options);
    }



    public void Notify() => OnChange?.Invoke();

    public void DeleteSelected()
    {
        if (SelectedNode != null)
        {
            // 1. Remove all connections attached to this node
            Connections.RemoveAll(c => c.Source == SelectedNode || c.Target == SelectedNode);
            // 2. Remove the node
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
        // Clear previous selections
        if (SelectedNode != null) SelectedNode.IsSelected = false;
        SelectedNode = null;
        SelectedConnection = null;

        // Apply new selection
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
