namespace Argent.Contracts.Workflows.Execution;

public record ClaimedWork(
    Guid WorkItemId,
    Guid TokenId,
    Guid InstanceId,
    Guid NodeId,
    string NodeType,
    Guid DefinitionId,
    byte RetryCount,
    byte MaxRetries,
    string? TokenPayload
);

public interface IWorkClaimer
{
    Task<IReadOnlyList<ClaimedWork>> ClaimAsync(int batchSize, CancellationToken ct);
}
