using Argent.Contracts.Workflows.Execution;

namespace Argent.Runtime.Workflows.Execution;

public class TokenExecutionContext(
    Guid instanceId,
    Guid tokenId,
    Guid nodeId,
    IVariableBag variables) : ITokenExecutionContext
{
    public Guid InstanceId { get; } = instanceId;
    public Guid TokenId { get; } = tokenId;
    public Guid NodeId { get; } = nodeId;
    public IVariableBag Variables { get; } = variables;
}
