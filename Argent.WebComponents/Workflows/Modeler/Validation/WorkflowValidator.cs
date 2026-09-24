using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.Modeler;

namespace Argent.WebComponents.Workflows.Modeler.Validation;

public class WorkflowValidator
{
    private ValidationResult? _validationResult;

    public ValidationResult Validate(WorkflowDefinition wf)
    {
        _validationResult = new();
        EnsureAllNodesCanReachAnEndEvent(wf);
        EnsureUserActivitiesHaveAssignees(wf);
        return _validationResult;
    }

    public void EnsureAllNodesCanReachAnEndEvent(WorkflowDefinition wf)
    {
        if (_validationResult == null)
            throw new InvalidOperationException("Validation result cannot be null.");

        var inboundLookup = wf.Connections
            .GroupBy(c => c.To.Id)
            .ToDictionary(g => g.Key, g => g.Select(c => c.From).ToList());

        var safeNodes = new HashSet<Guid>();
        var queue = new Queue<NodeBase>();

        foreach (var node in wf.Nodes.Where(n => n is EndEvent))
        {
            safeNodes.Add(node.Id);
            queue.Enqueue(node);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!inboundLookup.TryGetValue(current.Id, out var incomingNodes)) continue;
            foreach (var upstreamNode in incomingNodes)
            {
                if (safeNodes.Add(upstreamNode.Id))
                    queue.Enqueue(upstreamNode);
            }
        }

        if (safeNodes.Count != wf.Nodes.Count)
        {
            foreach (var deadEndNode in wf.Nodes.Where(n => !safeNodes.Contains(n.Id)))
                _validationResult.AddError(deadEndNode, "Node cannot reach an EndEvent");
        }
    }

    public void EnsureUserActivitiesHaveAssignees(WorkflowDefinition wf)
    {
        foreach (var node in wf.Nodes.OfType<UserActivity>())
        {
            if (node.LaneRoleId == null)
                _validationResult!.AddError(node, "User task must be placed in a role lane");
        }
    }
}
