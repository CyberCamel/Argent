namespace Argent.Contracts.Workflows.Execution;

public interface ITokenExecutionContext
{
    Guid InstanceId { get; }
    Guid TokenId { get; }
    Guid NodeId { get; }
    IVariableBag Variables { get; }
}
