using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Execution;

public class RecoveryPassTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public RecoveryPassTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    private TestArgentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new TestArgentDbContext(options);
    }

    private RecoveryPass CreateRecovery()
    {
        var factory = new Mock<IDbContextFactory<ArgentDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateContext);
        var timerManager = new TimerManager(factory.Object, Mock.Of<ILogger<TimerManager>>());
        return new RecoveryPass(factory.Object, timerManager, Mock.Of<ILogger<RecoveryPass>>());
    }

    private static WorkItem WorkItem(Guid instanceId, WorkItemState state, Action<WorkItem>? mutate = null)
    {
        var wi = new WorkItem
        {
            Id = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            NodeType = "JintActivity",
            TokenId = Guid.NewGuid(),
            State = state,
            CreatedAt = DateTime.UtcNow,
        };
        mutate?.Invoke(wi);
        return wi;
    }

    [Fact]
    public async Task Releases_stale_lock_back_to_pending_and_increments_retry()
    {
        var wi = WorkItem(Guid.NewGuid(), WorkItemState.Running, w =>
        {
            w.LockedBy = "machine-1";
            w.LockExpirationUtc = DateTime.UtcNow.AddMinutes(-10);
            w.RetryCount = 0;
            w.MaxRetries = 3;
        });

        await using (var seed = CreateContext())
        {
            seed.WorkItems.Add(wi);
            await seed.SaveChangesAsync();
        }

        await CreateRecovery().RunAsync(default);

        await using var check = CreateContext();
        var updated = await check.WorkItems.FindAsync(wi.Id);
        Assert.Equal(WorkItemState.Pending, updated!.State);
        Assert.Equal(1, updated.RetryCount);
        Assert.Null(updated.LockedBy);
        Assert.Null(updated.LockExpirationUtc);
    }

    [Fact]
    public async Task Dead_letters_stale_item_past_max_retries()
    {
        var wi = WorkItem(Guid.NewGuid(), WorkItemState.Running, w =>
        {
            w.LockExpirationUtc = DateTime.UtcNow.AddMinutes(-10);
            w.RetryCount = 3;
            w.MaxRetries = 3;
        });

        await using (var seed = CreateContext())
        {
            seed.WorkItems.Add(wi);
            await seed.SaveChangesAsync();
        }

        await CreateRecovery().RunAsync(default);

        await using var check = CreateContext();
        var updated = await check.WorkItems.FindAsync(wi.Id);
        Assert.Equal(WorkItemState.Failed, updated!.State);
    }

    [Fact]
    public async Task Requeues_orphan_ready_token_when_instance_exists()
    {
        var instanceId = Guid.NewGuid();
        var token = new WorkflowToken
        {
            Id = Guid.NewGuid(),
            InstanceId = instanceId,
            NodeId = Guid.NewGuid(),
            State = TokenState.Ready,
            CreatedAt = DateTime.UtcNow,
        };

        await using (var seed = CreateContext())
        {
            seed.WorkflowInstances.Add(new WorkflowInstance
            {
                InstanceId = instanceId,
                WorkflowId = Guid.NewGuid(),
                State = InstanceState.Running,
                StartTime = DateTime.UtcNow,
            });
            seed.WorkflowTokens.Add(token);
            await seed.SaveChangesAsync();
        }

        await CreateRecovery().RunAsync(default);

        await using var check = CreateContext();
        var created = await check.WorkItems.FirstOrDefaultAsync(w => w.TokenId == token.Id);
        Assert.NotNull(created);
        Assert.Equal(WorkItemState.Pending, created!.State);
    }

    [Fact]
    public async Task Consumes_orphan_token_when_instance_missing()
    {
        var token = new WorkflowToken
        {
            Id = Guid.NewGuid(),
            InstanceId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            State = TokenState.Ready,
            CreatedAt = DateTime.UtcNow,
        };

        await using (var seed = CreateContext())
        {
            seed.WorkflowTokens.Add(token);
            await seed.SaveChangesAsync();
        }

        await CreateRecovery().RunAsync(default);

        await using var check = CreateContext();
        var updated = await check.WorkflowTokens.FindAsync(token.Id);
        Assert.Equal(TokenState.Consumed, updated!.State);
    }

    [Fact]
    public async Task Completes_instance_with_no_active_tokens()
    {
        var instanceId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.WorkflowInstances.Add(new WorkflowInstance
            {
                InstanceId = instanceId,
                WorkflowId = Guid.NewGuid(),
                State = InstanceState.Running,
                StartTime = DateTime.UtcNow,
            });
            // All tokens are consumed — the EndEvent fired but the post-commit completion
            // check was lost (e.g. crash). Recovery detects this and marks the instance Completed.
            seed.WorkflowTokens.Add(new WorkflowToken
            {
                Id = Guid.NewGuid(),
                InstanceId = instanceId,
                NodeId = Guid.NewGuid(),
                State = TokenState.Consumed,
                CreatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await CreateRecovery().RunAsync(default);

        await using var check = CreateContext();
        var instance = await check.WorkflowInstances.FindAsync(instanceId);
        Assert.Equal(InstanceState.Completed, instance!.State);
        Assert.NotNull(instance.EndTime);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
