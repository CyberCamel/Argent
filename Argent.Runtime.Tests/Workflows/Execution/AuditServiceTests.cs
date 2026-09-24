using Argent.Infrastructure.Data;
using Argent.Runtime.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Execution;

public class AuditServiceTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly IDbContextFactory<ArgentDbContext> _factory;

    public AuditServiceTests()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;

        _factory = new TestDbContextFactory(options);
    }

    [Fact]
    public async Task RecordAsync_writes_journal_entry()
    {
        var service = new AuditService(_factory);
        var instanceId = Guid.NewGuid();

        await service.RecordAsync(
            "Workflow", "TestEvent",
            instanceId: instanceId,
            actor: "system",
            details: new { note = "hello" });

        await using var ctx = _factory.CreateDbContext();
        var entries = await ctx.WorkflowJournalEntries.ToListAsync();
        Assert.Single(entries);
        Assert.Equal("Workflow", entries[0].Category);
        Assert.Equal("TestEvent", entries[0].EventType);
        Assert.Equal(instanceId, entries[0].InstanceId);
        Assert.Equal("system", entries[0].Actor);
    }

    [Fact]
    public async Task RecordAsync_can_store_multiple_entries()
    {
        var service = new AuditService(_factory);

        await service.RecordAsync("Workflow", "Event1");
        await service.RecordAsync("Workflow", "Event2");
        await service.RecordAsync("Security", "Login", actor: "alice");

        await using var ctx = _factory.CreateDbContext();
        var count = await ctx.WorkflowJournalEntries.CountAsync();
        Assert.Equal(3, count);
    }

    public void Dispose()
    {
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureDeleted();
    }

    private class TestDbContextFactory(DbContextOptions<ArgentDbContext> options)
        : IDbContextFactory<ArgentDbContext>
    {
        public ArgentDbContext CreateDbContext()
        {
            return new ArgentDbContext(options);
        }

        public async Task<ArgentDbContext> CreateDbContextAsync(CancellationToken ct = default)
        {
            await Task.CompletedTask;
            return new ArgentDbContext(options);
        }
    }
}
