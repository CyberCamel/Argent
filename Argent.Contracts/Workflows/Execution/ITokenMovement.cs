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
    WorkflowJournalEntry? JournalEntry,
    // True only when the consuming node is a terminal (EndEvent) node. Instance completion
    // is evaluated solely on terminal nodes so a non-end node that produces zero targets
    // cannot silently complete the instance.
    bool IsTerminal = false
);

public interface ITokenMovement
{
    Task CommitAsync(TokenMovementRequest request, CancellationToken ct);
}
