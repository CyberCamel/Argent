using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;

namespace Argent.Runtime.Workflows.Handlers;

public class EndEventHandler : INodeHandler
{
    public Type HandledNodeType => typeof(EndEvent);

    public Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        return Task.FromResult(new NodeResult(true));
    }
}
