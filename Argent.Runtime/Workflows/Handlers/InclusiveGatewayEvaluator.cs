using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;

namespace Argent.Runtime.Workflows.Handlers;

public class InclusiveGatewayEvaluator : INodeHandler
{
    public Type HandledNodeType => typeof(InclusiveGateway);

    public Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var matchedTargets = new List<Guid>();

        foreach (var target in ctx.CandidateTargets)
        {
            if (target.Expression == null)
                continue;

            if (EvaluateCondition(target.Expression, ctx.Variables))
            {
                matchedTargets.Add(target.NodeId);
            }
        }

        if (matchedTargets.Count == 0)
        {
            var defaultPath = ctx.CandidateTargets.FirstOrDefault(t => t.Expression == null);
            if (defaultPath != null)
                matchedTargets.Add(defaultPath.NodeId);
        }

        return Task.FromResult(new NodeResult(
            matchedTargets.Count > 0,
            matchedTargets.Count == 0 ? "No matching path in inclusive gateway" : null,
            ExplicitTargetNodeIds: matchedTargets));
    }

    private static bool EvaluateCondition(string expression, IVariableBag variables)
    {
        var expr = new NCalc.Expression(expression);
        expr.EvaluateParameter += (name, args) =>
        {
            args.Result = variables.Get(name);
        };
        var result = expr.Evaluate();
        return result is true;
    }
}
