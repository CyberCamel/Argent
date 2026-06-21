using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
using Argent.Models.Workflows.Modeler;

namespace Argent.Runtime.Workflows.Modeling.Validation;

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
        {
            throw  new InvalidOperationException("Validation result cannot be null.");
        }
        
        // 1. Group connections by their destination ('To') 
        // This allows us to quickly find all nodes that flow *into* a specific target node ID.
        var inboundLookup = wf.Connections
            .GroupBy(c => c.To.Id)
            .ToDictionary(g => g.Key, g => g.Select(c => c.From).ToList());

        var safeNodes = new HashSet<Guid>();
        var queue = new Queue<NodeBase>();

        // 2. Find all End Events to use as our starting seeds
        foreach (var node in wf.Nodes.Where(n => n is EndEvent))
        {
            safeNodes.Add(node.Id);
            queue.Enqueue(node);
        }

        // 3. Traverse backward up the workflow stream
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            // Find all nodes that have a connection pointing TO the current node
            if (!inboundLookup.TryGetValue(current.Id, out var incomingNodes)) continue;
            foreach (var upstreamNode in incomingNodes)
            {
                // If we haven't marked this upstream node as safe yet, queue it up
                if (safeNodes.Add(upstreamNode.Id))
                {
                    queue.Enqueue(upstreamNode);
                }
            }
        }

        // 4. Any node left out of the safe set is a dead end
        if (safeNodes.Count != wf.Nodes.Count)
        {
            var deadEndNodes = wf.Nodes.Where(n => !safeNodes.Contains(n.Id));
            foreach (var deadEndNode in deadEndNodes)
            {
                _validationResult.AddError(deadEndNode, "Node cannot reach an EndEvent");
            }
        }
    }

    public void EnsureUserActivitiesHaveAssignees(WorkflowDefinition wf)
    {
        foreach (var node in wf.Nodes.OfType<UserActivity>())
        {
            if (string.IsNullOrWhiteSpace(node.AssigneeExpression) &&
                node.LaneRoleId == null)
            {
                _validationResult!.AddError(node, "User task must have an assignee expression or be placed in a role lane");
            }
        }
    }

    public void DetectInfiniteLoop(WorkflowDefinition wf)
    {

    }
    
    public void DetectUnconfiguredActivity(WorkflowDefinition wf)
    {
    }
    
    public void EnsureStartEventExists(WorkflowDefinition wf)
    {
    }

    public void EnsureNoOrphanedNodes(WorkflowDefinition wf)
    {
        
    }
    
    
    
    
    
}