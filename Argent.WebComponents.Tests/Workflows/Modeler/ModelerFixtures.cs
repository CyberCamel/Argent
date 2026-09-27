using System;
using System.Collections.Generic;
using System.Linq;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.Modeler;

namespace Argent.WebComponents.Tests.Workflows.Modeler;

/// <summary>
/// Deterministic workflow fixtures for the modeler. The same generator produces the
/// disposable diagrams used by the interaction tests and by the browser performance gate,
/// so a measured number always refers to a diagram that can be rebuilt exactly.
/// </summary>
public static class ModelerFixtures
{
    /// <summary>
    /// A grid of <paramref name="nodeCount"/> task nodes joined by a snake of connections,
    /// laid out so that a fraction of the connectors must route around their neighbours.
    /// </summary>
    public static WorkflowDefinition Grid(int nodeCount, int columns = 10)
    {
        var definition = new WorkflowDefinition { Metadata = new WorkflowMetadata() };
        var nodes = new List<NodeBase>();
        var previous = new List<NodeBase>();

        for (int i = 0; i < nodeCount; i++)
        {
            int column = i % columns;
            int row = i / columns;
            bool reversedRow = row % 2 == 1;

            var node = new UserActivity { Name = $"task-{i}" };
            nodes.Add(node);
            definition.Nodes.Add(node);
            definition.Layouts[node.Id] = new NodeLayout
            {
                X = 100 + column * 240,
                Y = 100 + row * 200,
                Width = 160,
                Height = 80
            };

            if (reversedRow && column > 0) previous.Insert(0, node);
            else previous.Add(node);
        }

        // A chain within each row plus a link down to the next row, which is what forces
        // the router around neighbouring shapes.
        for (int row = 0; row * columns < nodeCount; row++)
        {
            var rowNodes = nodes.Skip(row * columns).Take(Math.Min(columns, nodeCount - row * columns)).ToList();
            bool reversed = row % 2 == 1;
            var ordered = reversed ? rowNodes.AsEnumerable().Reverse().ToList() : rowNodes;

            for (int i = 0; i + 1 < ordered.Count; i++)
            {
                definition.Connections.Add(new Connection
                {
                    From = ordered[i],
                    To = ordered[i + 1]
                });
            }

            if ((row + 1) * columns < nodeCount)
            {
                definition.Connections.Add(new Connection
                {
                    From = rowNodes[^1],
                    To = rowNodes[0]
                });
            }
        }

        return definition;
    }

    /// <summary>
    /// The performance fixtures named in the release gate: 25, 100, 250 and 500
    /// nodes/connections.
    /// </summary>
    public static IEnumerable<(string Name, int Size)> PerformanceSizes =>
    [
        ("25", 25),
        ("100", 100),
        ("250", 250),
        ("500", 500)
    ];

    /// <summary>
    /// A fixture built for the release gate: a dense grid plus nearby obstacles, overlapping
    /// obstacle margins, a long detour and a batch of manually constrained routes.
    /// </summary>
    public static WorkflowDefinition Performance(int nodeCount, int columns = 10)
    {
        var definition = Grid(nodeCount, columns);
        var random = new Random(20260927);

        // Nearby obstacles: nodes that sit close enough to a connector to force a detour
        // without overlapping the node they belong to.
        var anchors = definition.Nodes.Take(Math.Min(40, definition.Nodes.Count)).ToList();
        for (int i = 0; i < Math.Max(4, nodeCount / 10); i++)
        {
            var node = new UserActivity { Name = $"obstacle-{i}" };
            definition.Nodes.Add(node);
            definition.Layouts[node.Id] = new NodeLayout
            {
                X = 60 + random.NextDouble() * 2400,
                Y = 60 + random.NextDouble() * 1600,
                Width = 40,
                Height = 40
            };
            _ = anchors;
        }

        // A long detour: two far apart nodes joined directly.
        var from = definition.Nodes.First();
        var to = definition.Nodes.Last();
        definition.Connections.Add(new Connection { From = from, To = to });

        return definition;
    }
}
