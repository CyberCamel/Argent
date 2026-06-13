using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows.Auditing;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Argent.Runtime.Workflows.Execution;

public class AuditService : IAuditService
{
    private readonly IDbContextFactory<ArgentDbContext> _contextFactory;

    public AuditService(IDbContextFactory<ArgentDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task RecordAsync(
        string category,
        string eventType,
        Guid? instanceId = null,
        Guid? tokenId = null,
        string? actor = null,
        object? details = null,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        context.WorkflowJournalEntries.Add(new WorkflowJournalEntry
        {
            Id = Guid.NewGuid(),
            Category = category,
            EventType = eventType,
            InstanceId = instanceId ?? Guid.Empty,
            TokenId = tokenId,
            Actor = actor,
            TimeStamp = DateTime.UtcNow,
            Details = details != null ? JsonSerializer.Serialize(details) : null
        });

        await context.SaveChangesAsync(ct);
    }
}
