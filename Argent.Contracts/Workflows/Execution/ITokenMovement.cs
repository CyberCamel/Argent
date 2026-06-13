using Argent.Models.Workflows.Auditing;

namespace Argent.Contracts.Workflows.Execution;

public record TokenTarget(
    Guid NodeId,
    string NodeType,
    IReadOnlyDictionary<string, object?>? Variables
);

public record TokenMovementRequest(
    Guid InstanceId,
    Guid ConsumedTokenId,
    Guid DefinitionId,
    IReadOnlyList<TokenTarget> Targets,
    WorkflowJournalEntry? JournalEntry
);

public interface ITokenMovement
{
    Task CommitAsync(TokenMovementRequest request, CancellationToken ct);
}
