using Argent.Contracts.DataSources;
using Argent.Contracts.Workflows.Execution;
using Argent.Models.DataSources;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;

namespace Argent.Runtime.Workflows.Handlers;

public class SQLActivityHandler : INodeHandler
{
    private readonly IDataSourceRunner _dataSourceRunner;

    public Type HandledNodeType => typeof(SQLActivity);

    public SQLActivityHandler(IDataSourceRunner dataSourceRunner)
    {
        _dataSourceRunner = dataSourceRunner;
    }

    public async Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var activity = (SQLActivity)node;

        try
        {
            var requestParams = new Dictionary<string, object?>();
            foreach (var kvp in activity.Parameters)
            {
                var value = ctx.Variables.Get(kvp.Value) ?? kvp.Value;
                requestParams[kvp.Key] = value;
            }

            // SqlRequest.Parameters expects Dictionary<string, object?>
            var sqlParameters = activity.Parameters
                .ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);

            var result = await _dataSourceRunner.ExecuteAsync(
                activity.ConnectionKey,
                new SqlRequest
                {
                    Query = activity.Query,
                    Parameters = sqlParameters
                },
                requestParams,
                ct);

            return new NodeResult(
                true,
                OutputVariables: new Dictionary<string, object?>
                {
                    ["result"] = result.Rows,
                    ["rowCount"] = result.Rows?.Count ?? 0
                });
        }
        catch (Exception ex)
        {
            return new NodeResult(false, ex.Message);
        }
    }
}
