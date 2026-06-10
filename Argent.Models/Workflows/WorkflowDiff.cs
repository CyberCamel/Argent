namespace Argent.Models.Workflows;

public class WorkflowDiff
{
    public List<DiffEntry> NodeAdditions { get; set; } = [];
    public List<DiffEntry> NodeRemovals { get; set; } = [];
    public List<DiffEntry> NodeModifications { get; set; } = [];
    public List<DiffEntry> ConnectionAdditions { get; set; } = [];
    public List<DiffEntry> ConnectionRemovals { get; set; } = [];

    public bool HasChanges =>
        NodeAdditions.Count > 0 || NodeRemovals.Count > 0 || NodeModifications.Count > 0 ||
        ConnectionAdditions.Count > 0 || ConnectionRemovals.Count > 0;

    public static WorkflowDiff Compare(WorkflowDefinition oldDef, WorkflowDefinition newDef)
    {
        var diff = new WorkflowDiff();
        var oldNodes = oldDef.Nodes.ToDictionary(n => n.Id);
        var newNodes = newDef.Nodes.ToDictionary(n => n.Id);

        foreach (var (id, newNode) in newNodes)
        {
            if (oldNodes.TryGetValue(id, out var oldNode))
            {
                var notes = new List<string>();
                var oldLayout = oldDef.Layouts?.GetValueOrDefault(id);
                var newLayout = newDef.Layouts?.GetValueOrDefault(id);

                if (oldLayout != null && newLayout != null)
                {
                    if (oldLayout.X != newLayout.X || oldLayout.Y != newLayout.Y)
                        notes.Add("position changed");
                    if (oldLayout.Width != newLayout.Width || oldLayout.Height != newLayout.Height)
                        notes.Add("resized");
                }

                if (oldNode.Name != newNode.Name)
                    notes.Add($"renamed to \"{newNode.Name}\"");

                if (notes.Count > 0)
                    diff.NodeModifications.Add(new DiffEntry { Description = $"{DisplayName(oldNode)}: {string.Join("; ", notes)}" });
            }
            else
            {
                diff.NodeAdditions.Add(new DiffEntry { Description = DisplayName(newNode) });
            }
        }

        foreach (var (id, oldNode) in oldNodes)
        {
            if (!newNodes.ContainsKey(id))
                diff.NodeRemovals.Add(new DiffEntry { Description = DisplayName(oldNode) });
        }

        var existingPairs = new HashSet<(Guid From, Guid To)>();
        foreach (var conn in oldDef.Connections)
            existingPairs.Add((conn.From.Id, conn.To.Id));

        foreach (var conn in newDef.Connections)
        {
            var pair = (conn.From.Id, conn.To.Id);
            if (!existingPairs.Contains(pair))
            {
                var fromName = newNodes.TryGetValue(conn.From.Id, out var fn) ? DisplayName(fn) : "?";
                var toName = newNodes.TryGetValue(conn.To.Id, out var tn) ? DisplayName(tn) : "?";
                diff.ConnectionAdditions.Add(new DiffEntry { Description = $"{fromName} → {toName}" });
            }
            else
                existingPairs.Remove(pair);
        }

        foreach (var pair in existingPairs)
        {
            var fromName = oldNodes.TryGetValue(pair.From, out var fn) ? DisplayName(fn) : "?";
            var toName = oldNodes.TryGetValue(pair.To, out var tn) ? DisplayName(tn) : "?";
            diff.ConnectionRemovals.Add(new DiffEntry { Description = $"{fromName} → {toName}" });
        }

        return diff;
    }

    private static string DisplayName(NodeBase node) => node.Name ?? node.GetType().Name;
}

public class DiffEntry
{
    public string Description { get; set; } = "";
}
