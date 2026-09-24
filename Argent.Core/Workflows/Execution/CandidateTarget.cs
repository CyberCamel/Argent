namespace Argent.Core.Workflows.Execution;

public record CandidateTarget(
    Guid NodeId,
    string NodeType,
    string? Expression,
    string? Label = null
);
