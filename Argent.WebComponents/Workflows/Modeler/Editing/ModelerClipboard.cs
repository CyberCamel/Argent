using System.Text.Json;
using Argent.Core.Workflows;
using Argent.Core.Forms.Components.Configuration;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.BoundaryEvents;
using Argent.Core.Workflows.Modeler;
using Argent.WebComponents.Workflows.Modeler.Interaction;
using Argent.WebComponents.Workflows.Modeler.Routing;

namespace Argent.WebComponents.Workflows.Modeler.Editing;

/// <summary>
/// Copy, paste and duplicate for the modeler. A copied node keeps its workflow payload,
/// but everything that identifies it inside this diagram is remapped on paste: the node
/// gets a new id, boundary events are re-attached to the new parent, and internal
/// connections are rebuilt with their own new identities and their own copy of the route
/// intent.
/// </summary>
public sealed class ModelerClipboard
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private Entry[]? _entries;

    public bool HasContent => _entries is { Length: > 0 };

    public void Copy(DesignerService state, IEnumerable<DesignerNode> nodes)
    {
        var selection = nodes.Where(n => state.Nodes.Contains(n)).ToList();
        if (selection.Count == 0) return;

        var index = selection.Select((node, i) => (node, i)).ToDictionary(p => p.node, p => p.i);
        var entries = selection.Select(node => new Entry
        {
            OriginalId = node.NodeData.Id,
            Node = JsonSerializer.Serialize(node.NodeData, JsonOptions)!,
            X = node.X,
            Y = node.Y,
            Width = node.Width,
            Height = node.Height,
            Title = node.Title,
            Description = node.Description,
            CssClass = node.CssClass,
            Shape = node.Shape
        }).ToArray();

        foreach (var connection in state.Connections)
        {
            if (!index.TryGetValue(connection.Source, out var sourceIndex)) continue;
            if (!index.TryGetValue(connection.Target, out var targetIndex)) continue;

            entries[sourceIndex].Connections.Add(new ConnectionEntry
            {
                Target = targetIndex,
                SourceDir = connection.SourceDir,
                TargetDir = connection.TargetDir,
                Label = connection.EngineConnection.Label,
                Expression = connection.EngineConnection.Expression,
                Condition = connection.EngineConnection.Condition,
                IsDefault = connection.EngineConnection.IsDefault,
                TaskAction = connection.EngineConnection.TaskAction,
                Route = connection.Route?.Clone()
            });
        }

        _entries = entries;
    }

    /// <summary>
    /// Adds copies of the clipboard contents offset by (dx, dy) from the original
    /// positions. The new nodes and their internal connections are returned so the caller
    /// can select them inside a single undo transaction.
    /// </summary>
    public (List<DesignerNode> Nodes, List<DesignerConnection> Connections) Paste(
        DesignerService state, double dx = 40, double dy = 40)
    {
        if (_entries == null) return ([], []);

        var remapped = new Dictionary<Guid, DesignerNode>();
        var created = new List<DesignerNode>();
        var editor = GeometryEditor.For(state);

        for (int i = 0; i < _entries.Length; i++)
        {
            var entry = _entries[i];
            var data = JsonSerializer.Deserialize<NodeBase>(entry.Node, JsonOptions);
            if (data == null) continue;

            // Everything that identifies the node inside a diagram is new.
            data.Id = Guid.NewGuid();
            data.Inbound = [];
            data.Outbound = [];

            var node = new DesignerNode
            {
                NodeData = data,
                Title = entry.Title,
                Description = entry.Description,
                CssClass = entry.CssClass,
                Shape = entry.Shape,
                X = entry.X + dx,
                Y = entry.Y + dy,
                Width = entry.Width,
                Height = entry.Height
            };

            state.Nodes.Add(node);
            created.Add(node);
            remapped[entry.OriginalId] = node;
        }

        // A boundary event may be listed before its parent, so re-link after every copy exists.
        // A parent that was not copied keeps pointing at the node it was already attached to.
        foreach (var node in created)
        {
            if (node.NodeData is not BoundaryEvent boundary) continue;
            if (remapped.TryGetValue(boundary.ParentNodeId, out var parent))
                boundary.ParentNodeId = parent.NodeData.Id;
        }

        var connections = new List<DesignerConnection>();
        for (int i = 0; i < _entries.Length; i++)
        {
            foreach (var entry in _entries[i].Connections)
            {
                if (entry.Target >= created.Count) continue;
                var source = created[i];
                var target = created[entry.Target];
                var connection = new DesignerConnection
                {
                    EngineConnection = new Connection
                    {
                        Id = Guid.NewGuid(),
                        From = source.NodeData,
                        To = target.NodeData,
                        Label = entry.Label,
                        Expression = entry.Expression,
                        Condition = entry.Condition,
                        IsDefault = entry.IsDefault,
                        TaskAction = entry.TaskAction,
                        Route = entry.Route?.Clone()
                    },
                    Source = source,
                    Target = target,
                    SourceDir = entry.SourceDir,
                    TargetDir = entry.TargetDir
                };
                state.Connections.Add(connection);
                connections.Add(connection);
            }
        }

        editor.RefreshRoutes(DesignerSnapshot.Capture(state), created);
        return (created, connections);
    }

    public void Clear() => _entries = null;

    private sealed class Entry
    {
        public Guid OriginalId { get; set; }
        public string Node { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string CssClass { get; set; } = "";
        public NodeShape Shape { get; set; }
        public List<ConnectionEntry> Connections { get; } = [];
    }

    private sealed class ConnectionEntry
    {
        public int Target { get; set; }
        public AnchorDirection SourceDir { get; set; }
        public AnchorDirection TargetDir { get; set; }
        public string? Label { get; set; }
        public string? Expression { get; set; }
        public Condition? Condition { get; set; }
        public bool IsDefault { get; set; }
        public TaskActionPresentation? TaskAction { get; set; }
        public ConnectionRoute? Route { get; set; }
    }
}
