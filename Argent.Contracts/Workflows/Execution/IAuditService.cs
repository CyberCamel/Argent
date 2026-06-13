namespace Argent.Contracts.Workflows.Execution;

public interface IAuditService
{
    Task RecordAsync(
        string category,
        string eventType,
        Guid? instanceId = null,
        Guid? tokenId = null,
        string? actor = null,
        object? details = null,
        CancellationToken ct = default);
}
