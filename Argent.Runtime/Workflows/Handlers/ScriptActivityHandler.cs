using Argent.Contracts.DomainObjects;
using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Workflows.Handlers;

public class ScriptActivityHandler : INodeHandler
{
    private readonly IDomainObjectStore _store;
    private readonly IDbContextFactory<ArgentDbContext> _dbFactory;

    public Type HandledNodeType => typeof(ScriptActivity);

    public ScriptActivityHandler(IDomainObjectStore store, IDbContextFactory<ArgentDbContext> dbFactory)
    {
        _store = store;
        _dbFactory = dbFactory;
    }

    public async Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var activity = (ScriptActivity)node;
        var outputVars = new Dictionary<string, object?>();

        foreach (var action in activity.Actions)
        {
            switch (action)
            {
                case SetVariableAction setVar:
                    if (!string.IsNullOrWhiteSpace(setVar.VariableName))
                        outputVars[setVar.VariableName] = setVar.Value;
                    break;

                case SetFormFieldAction setField:
                    await ExecuteSetFormFieldAsync(ctx, setField, ct);
                    break;
            }
        }

        return new NodeResult(true, OutputVariables: outputVars.Count > 0 ? outputVars : null);
    }

    private async Task ExecuteSetFormFieldAsync(ITokenExecutionContext ctx, SetFormFieldAction action, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(action.FieldKey)) return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.InstanceId == ctx.InstanceId, ct);

        if (instance == null || instance.RecordId == Guid.Empty) return;

        var version = await db.WorkflowVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == instance.VersionId, ct);

        if (version?.Definition == null) return;

        var startEvent = version.Definition.Nodes.OfType<StartEvent>().FirstOrDefault();
        if (startEvent == null || string.IsNullOrWhiteSpace(startEvent.ObjectKey)) return;

        await _store.UpdateAsync(
            startEvent.ObjectKey,
            instance.RecordId,
            new Dictionary<string, object?> { [action.FieldKey] = action.Value });
    }
}
