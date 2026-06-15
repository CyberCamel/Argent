using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;

namespace Argent.Runtime.Workflows.Handlers;

public class ExclusiveGatewayEvaluator : INodeHandler
{
    public Type HandledNodeType => typeof(ExclusiveGateway);

    public Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        CandidateTarget? defaultTarget = null;

        foreach (var target in ctx.CandidateTargets)
        {
            if (target.Expression == null)
            {
                defaultTarget ??= target;
                continue;
            }

            if (EvaluateCondition(target.Expression, ctx.Variables))
                return Task.FromResult(new NodeResult(true, ExplicitTargetNodeIds: [target.NodeId]));
        }

        if (defaultTarget != null)
            return Task.FromResult(new NodeResult(true, ExplicitTargetNodeIds: [defaultTarget.NodeId]));

        return Task.FromResult(new NodeResult(
            false,
            "No matching path in exclusive gateway",
            ResultType: NodeResultType.Failed));
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
