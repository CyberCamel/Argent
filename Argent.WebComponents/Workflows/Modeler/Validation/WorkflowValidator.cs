using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workers;
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
        EnsureWorkerActivitiesAreRoutable(wf);
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

    /// <summary>
    /// A worker node that names no worker, or no subject, can never be dispatched. These are
    /// design errors rather than runtime surprises, so they block publishing instead of failing
    /// the token halfway through a live instance.
    /// </summary>
    public void EnsureWorkerActivitiesAreRoutable(WorkflowDefinition wf)
    {
        foreach (var node in wf.Nodes.OfType<WorkerActivity>())
        {
            if (string.IsNullOrWhiteSpace(node.WorkerName))
            {
                _validationResult!.AddError(node, "Worker activity must name the worker that should run it");
            }

            if (string.IsNullOrWhiteSpace(node.Subject))
            {
                _validationResult!.AddError(node, "Worker activity must name a subject the worker has a handler for");
            }

            foreach (var parameter in node.Parameters)
            {
                if (string.IsNullOrWhiteSpace(parameter.Key))
                    _validationResult!.AddWarning(node, "A worker parameter has no key and will not be sent");
            }

            if (node.MaxAttempts is < 1 or > 10)
            {
                _validationResult!.AddWarning(node, "Attempts should be between 1 and 10");
            }
        }
    }
}
