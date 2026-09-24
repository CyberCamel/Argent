using Argent.Core.Workflows.Execution;
using Argent.Core.Workflows;

namespace Argent.Runtime.Workflows.Handlers;

public class EndEventHandler : INodeHandler
{
    public Type HandledNodeType => typeof(EndEvent);

    public Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        return Task.FromResult(new NodeResult(true));
    }
}
