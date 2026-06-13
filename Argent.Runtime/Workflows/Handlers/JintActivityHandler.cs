using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
using Jint;
using System.Text.Json;

namespace Argent.Runtime.Workflows.Handlers;

public class JintActivityHandler : INodeHandler
{
    public Type HandledNodeType => typeof(JintActivity);

    public Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var activity = (JintActivity)node;

        try
        {
            var engine = new Engine(options =>
            {
                options.TimeoutInterval(TimeSpan.FromSeconds(30));
                options.LimitMemory(10_000_000);
            });

            foreach (var param in activity.Parameters)
            {
                engine.SetValue(param.Key, param.Value);
            }

            var snapshot = ctx.Variables.Snapshot();
            foreach (var kvp in snapshot)
            {
                if (kvp.Value is JsonElement je)
                    engine.SetValue(kvp.Key, je);
                else
                    engine.SetValue(kvp.Key, kvp.Value);
            }

            var result = engine.Evaluate(activity.Code);

            if (!string.IsNullOrWhiteSpace(activity.ReturnVariable))
            {
                var output = new Dictionary<string, object?>
                {
                    [activity.ReturnVariable] = result.IsNull() ? null : result.ToObject()
                };
                return Task.FromResult(new NodeResult(true, OutputVariables: output));
            }

            return Task.FromResult(new NodeResult(true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new NodeResult(false, ex.Message));
        }
    }
}
