namespace Argent.Contracts.Workflows.Execution;

public record ClaimedWork(
    Guid WorkItemId,
    Guid TokenId,
    Guid NodeId,
    string NodeType,
    byte RetryCount,
    byte MaxRetries
);

public interface IWorkClaimer
{
    Task<IReadOnlyList<ClaimedWork>> ClaimAsync(int batchSize, CancellationToken ct);
}
