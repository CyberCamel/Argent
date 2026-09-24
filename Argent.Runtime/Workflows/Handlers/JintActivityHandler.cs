using Argent.Core.Workflows.Execution;
using Argent.Core.Workflows;
using Argent.Core.DomainObjects;
using Argent.Core.Forms;
using Argent.Core.Workflows.Activities;
using Argent.Runtime.Workflows.Execution;
using Jint;
using System.Text.Json;

namespace Argent.Runtime.Workflows.Handlers;

public class JintActivityHandler : INodeHandler
{
    private readonly IDomainObjectStore? _domainObjectStore;
    private readonly IFormDataStore? _formDataStore;
    private readonly IDomainObjectDefinitionService? _domainDefinitions;

    public JintActivityHandler()
    {
    }

    public JintActivityHandler(
        IDomainObjectStore domainObjectStore,
        IFormDataStore formDataStore,
        IDomainObjectDefinitionService domainDefinitions)
    {
        _domainObjectStore = domainObjectStore;
        _formDataStore = formDataStore;
        _domainDefinitions = domainDefinitions;
    }

    public Type HandledNodeType => typeof(JintActivity);

    public Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var activity = (JintActivity)node;

        try
        {
            var engine = new Engine(options =>
            {
                options.TimeoutInterval(TimeSpan.FromSeconds(30));
                //options.MaxStatements(100_000_000);
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

            var scriptApi = new JintWorkflowScriptApi(
                ctx,
                _domainObjectStore,
                _formDataStore,
                _domainDefinitions);
            engine.SetValue("argent", scriptApi);

            var result = engine.Evaluate(activity.Code);
            var output = new Dictionary<string, object?>(scriptApi.InstanceUpdates, StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(activity.ReturnVariable))
                output[activity.ReturnVariable] = result.IsNull() ? null : result.ToObject();

            return Task.FromResult(new NodeResult(
                true,
                OutputVariables: output.Count == 0 ? null : output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new NodeResult(false, ex.Message, ResultType: NodeResultType.Failed));
        }
    }
}
