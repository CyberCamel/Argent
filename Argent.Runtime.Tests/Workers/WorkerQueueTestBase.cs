using Argent.Core.Workers;
using Argent.Infrastructure.Data;
using Argent.Runtime.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Argent.Runtime.Tests.Workers;

/// <summary>
/// SQLite-backed harness for the worker request queue. The claim path is raw T-SQL and is covered
/// separately against a real SQL Server; everything else here runs provider-neutrally.
/// </summary>
public abstract class WorkerQueueTestBase : IDisposable
{
    private readonly SqliteConnection _connection;

    protected WorkerQueueTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    protected WorkerRequestQueue Queue { get; private set; } = null!;

    protected WorkerRegistry Registry { get; private set; } = null!;

    protected void BuildQueue()
    {
        var factory = new Mock<IDbContextFactory<ArgentDbContext>>();
        factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateContext());
        Queue = new WorkerRequestQueue(
            factory.Object,
            "DataSource=:memory:",
            Mock.Of<Microsoft.Extensions.Logging.ILogger<WorkerRequestQueue>>());

        Registry = new WorkerRegistry(
            factory.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<WorkerRegistry>>());
    }

    protected static WorkerRequest NewRequest(
        string workerName = "reports",
        string subject = "generate",
        Guid? tokenId = null,
        Guid? workItemId = null,
        byte maxAttempts = 1,
        int timeoutSeconds = 900) => new()
    {
        Id = Guid.NewGuid(),
        InstanceId = Guid.NewGuid(),
        TokenId = tokenId ?? Guid.NewGuid(),
        WorkItemId = workItemId ?? Guid.NewGuid(),
        NodeId = Guid.NewGuid(),
        WorkerName = workerName,
        Subject = subject,
        Parameters = "{}",
        State = WorkerRequestState.Pending,
        MaxAttempts = maxAttempts,
        TimeoutSeconds = timeoutSeconds,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>Seeds a Waiting work item and returns its id, so release-on-completion can be observed.</summary>
    protected async Task<Guid> SeedWaitingWorkItemAsync(Guid tokenId)
    {
        var workItemId = Guid.NewGuid();
        await using var db = CreateContext();
        db.WorkItems.Add(new Core.Workflows.Execution.WorkItem
        {
            Id = workItemId,
            TokenId = tokenId,
            NodeId = Guid.NewGuid(),
            NodeType = "WorkerActivity",
            State = Core.Workflows.Execution.WorkItemState.Waiting,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return workItemId;
    }

    protected async Task<Core.Workflows.Execution.WorkItemState> GetWorkItemStateAsync(Guid workItemId)
    {
        await using var db = CreateContext();
        var item = await db.WorkItems.FindAsync(workItemId);
        return item?.State ?? Core.Workflows.Execution.WorkItemState.Completed;
    }

    protected TestWorkerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new TestWorkerDbContext(options);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Strips nvarchar(max) column types so the model can be created on SQLite.</summary>
public class TestWorkerDbContext(DbContextOptions<ArgentDbContext> options) : ArgentDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.GetColumnType() == "nvarchar(max)")
                    property.SetColumnType(null);
            }
        }
    }
}
