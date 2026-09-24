using Argent.Core.Workflows.Auditing;

namespace Argent.Core.Workflows.Execution;

public record TokenTarget(
    Guid NodeId,
    string NodeType,
    IReadOnlyDictionary<string, object?>? Variables
);

public record TokenMovementRequest(
    Guid InstanceId,
    Guid ConsumedTokenId,
    IReadOnlyList<TokenTarget> Targets,
    WorkflowJournalEntry? JournalEntry,
    bool IsTerminal = false,
    IReadOnlyDictionary<string, object?>? ProcessVariables = null
);

public interface ITokenMovement
{
    Task CommitAsync(TokenMovementRequest request, CancellationToken ct);
}
