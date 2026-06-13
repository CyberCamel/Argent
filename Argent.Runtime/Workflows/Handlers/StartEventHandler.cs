using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;

namespace Argent.Runtime.Workflows.Handlers;

public class StartEventHandler : INodeHandler
{
    public Type HandledNodeType => typeof(StartEvent);

    public Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        return Task.FromResult(new NodeResult(true));
    }
}
