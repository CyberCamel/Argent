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
            Nodes = Nodes.Select(n => n.NodeData).ToList()
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

    // Logic: Find where a wire should land on a node edge

    private (double X, double Y) GetBaseAnchor(DesignerNode node, AnchorDirection dir)
    {
        return dir switch
        {
            AnchorDirection.Left => (node.X, node.Y + (node.Height / 2)),
            AnchorDirection.Right => (node.X + node.Width, node.Y + (node.Height / 2)),
            AnchorDirection.Top => (node.X + (node.Width / 2), node.Y),
            AnchorDirection.Bottom => (node.X + (node.Width / 2), node.Y + node.Height),
            _ => (node.X, node.Y)
        };
    }
    public (double X, double Y) GetAnchorWorldPos(DesignerNode node, AnchorDirection dir, DesignerConnection? current = null)
    {
        var (baseX, baseY) = GetBaseAnchor(node, dir);

        if (current != null)
        {
            // Find all connections sharing this specific node + side
            var siblings = Connections.Where(c =>
                (c.Target == node && c.TargetDir == dir) ||
                (c.Source == node && c.SourceDir == dir)).ToList();

            if (siblings.Count > 1)
            {
                int index = siblings.IndexOf(current);
                double offset = (index - (siblings.Count - 1) / 2.0) * 0.1; // 10% step

                // If it's a vertical side, offset the Y. If horizontal, offset the X.
                if (dir == AnchorDirection.Left || dir == AnchorDirection.Right)
                    baseY += (offset * node.Height);
                else
                    baseX += (offset * node.Width);
            }
        }
        return (baseX, baseY);
    }

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
