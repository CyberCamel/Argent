using Argent.Core.Workflows.Execution;
using Argent.Core.Workflows;

namespace Argent.Runtime.Workflows.Handlers;

public class StartEventHandler : INodeHandler
{
    public Type HandledNodeType => typeof(StartEvent);

    public Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        return Task.FromResult(new NodeResult(true));
    }
}
