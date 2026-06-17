using Argent.Contracts.Workflows;
using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Enums;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Argent.Runtime.Tests.Workflows.Execution;

/// <summary>
/// Test-only DbContext that strips nvarchar(max) column types
/// for SQLite compatibility.
/// </summary>
public class TestArgentDbContext : ArgentDbContext
{
    public TestArgentDbContext(DbContextOptions<ArgentDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.GetColumnType() == "nvarchar(max)")
                {
                    property.SetColumnType(null);
                }
            }
        }
    }
}

public abstract class IntegrationTestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    private ServiceProvider? _serviceProvider;

    protected IntegrationTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    /// <summary>
    /// Seeds a workflow version, instance, and an initial token+work-item at the StartEvent.
    /// </summary>
    protected async Task<WorkflowTestSeed> SeedWorkflowAsync(
        WorkflowDefinition definition,
        Dictionary<string, object?>? initialVariables = null)
    {
        await using var db = CreateContext();

        var workflowId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        var workflow = new Workflow
        {
            Id = workflowId,
            Name = "Test Workflow",
            Description = "",
            CreatedOn = DateTime.UtcNow,
            UpdatedOn = DateTime.UtcNow,
            Tags = [],
        };

        var version = new WorkflowVersion
        {
            Id = versionId,
            WorkflowId = workflowId,
            Workflow = workflow,
            Definition = definition,
            State = WorkflowDefinitionState.Deployed,
            Version = new Version(1, 0),
            CreatedAt = DateTime.UtcNow,
        };

        var instance = new WorkflowInstance
        {
            InstanceId = instanceId,
            WorkflowId = workflowId,
            VersionId = versionId,
            State = InstanceState.Running,
            StartTime = DateTime.UtcNow,
        };

        db.Workflows.Add(workflow);
        db.WorkflowVersions.Add(version);
        db.WorkflowInstances.Add(instance);

        var startNode = definition.Nodes.OfType<StartEvent>().First();
        var tokenId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var payload = initialVariables != null
            ? System.Text.Json.JsonSerializer.Serialize(initialVariables)
            : "{}";

        db.WorkflowTokens.Add(new WorkflowToken
        {
            Id = tokenId,
            InstanceId = instanceId,
            NodeId = startNode.Id,
            State = TokenState.Ready,
            Payload = payload,
            CreatedAt = DateTime.UtcNow,
        });
        db.WorkItems.Add(new WorkItem
        {
            Id = workItemId,
            TokenId = tokenId,
            NodeId = startNode.Id,
            NodeType = nameof(StartEvent),
            State = WorkItemState.Pending,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        return new WorkflowTestSeed
        {
            WorkflowId = workflowId,
            VersionId = versionId,
            InstanceId = instanceId,
        };
    }

    /// <summary>
    /// Builds a real TokenRunner wired to the SQLite-backed DB and real handlers.
    /// </summary>
    protected TokenRunner CreateRunner()
    {
        var services = new ServiceCollection();

        services.AddScoped<ArgentDbContext>(_ => CreateContext());
        services.AddScoped<ITokenMovement, TokenMovement>();
        services.AddScoped<IEnumerable<INodeHandler>>(_ =>
        [
            new StartEventHandler(),
            new EndEventHandler(),
            new ExclusiveGatewayEvaluator(),
            new ParallelGatewayEvaluator(),
            new InclusiveGatewayEvaluator(),
        ]);

        var factoryMock = new Mock<IDbContextFactory<ArgentDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateContext);
        services.AddSingleton<IDbContextFactory<ArgentDbContext>>(factoryMock.Object);

        services.AddSingleton<ILogger<TokenRunner>>(Mock.Of<ILogger<TokenRunner>>());

        _serviceProvider = services.BuildServiceProvider();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<ArgentDbContext>>();
        var logger = _serviceProvider.GetRequiredService<ILogger<TokenRunner>>();

        return new TokenRunner(scopeFactory, null!, contextFactory, logger);
    }

    /// <summary>
    /// Takes the next Pending work item for the instance, claims it, and runs it through the TokenRunner.
    /// Returns the consumed token and completed work item (or null if none pending).
    /// </summary>
    protected async Task<(WorkflowToken? Token, WorkItem? WorkItem, IReadOnlyList<WorkItem> Created)> AdvanceAsync(
        TokenRunner runner,
        Guid instanceId,
        Guid workflowId,
        CancellationToken ct = default)
    {
        await using var db = CreateContext();

        var pending = await db.WorkItems
            .Where(wi => db.WorkflowTokens.Any(t => t.Id == wi.TokenId && t.InstanceId == instanceId)
                      && wi.State == WorkItemState.Pending)
            .OrderBy(wi => wi.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (pending == null)
            return (null, null, []);

        var token = await db.WorkflowTokens.FindAsync([pending.TokenId], ct);

        // Snapshot existing work-item ids so "created" can be detected by id diff.
        // A timestamp-based diff is unreliable: with parallel branches a sibling
        // branch's still-pending work item was created later and would be miscounted.
        var existingIds = await db.WorkItems
            .Where(wi => db.WorkflowTokens.Any(t => t.Id == wi.TokenId && t.InstanceId == instanceId))
            .Select(wi => wi.Id)
            .ToListAsync(ct);
        var existingIdSet = existingIds.ToHashSet();

        pending.State = WorkItemState.Running;
        await db.SaveChangesAsync(ct);

        var claimed = new ClaimedWork(
            pending.Id,
            pending.TokenId,
            pending.NodeId,
            pending.NodeType,
            0,
            3);

        await runner.RunAsync(claimed, ct);

        await using var verifyDb = CreateContext();
        var updatedToken = await verifyDb.WorkflowTokens.FindAsync([pending.TokenId], ct);
        var updatedWorkItem = await verifyDb.WorkItems.FindAsync([pending.Id], ct);

        var created = await verifyDb.WorkItems
            .Where(wi => verifyDb.WorkflowTokens.Any(t => t.Id == wi.TokenId && t.InstanceId == instanceId)
                      && !existingIdSet.Contains(wi.Id)
                      && wi.State == WorkItemState.Pending)
            .OrderBy(wi => wi.CreatedAt)
            .ToListAsync(ct);

        return (updatedToken, updatedWorkItem, created);
    }

    protected async Task<InstanceState> GetInstanceStateAsync(Guid instanceId)
    {
        await using var db = CreateContext();
        var instance = await db.WorkflowInstances.FindAsync([instanceId]);
        return instance?.State ?? InstanceState.Failed;
    }

    protected async Task<List<WorkflowToken>> GetTokensAsync(Guid instanceId)
    {
        await using var db = CreateContext();
        return await db.WorkflowTokens
            .Where(t => t.InstanceId == instanceId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
    }

    protected TestArgentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseSqlite(_connection)
            .Options;
        var ctx = new TestArgentDbContext(options);
        return ctx;
    }
}

public class WorkflowTestSeed
{
    public Guid WorkflowId { get; set; }
    public Guid VersionId { get; set; }
    public Guid InstanceId { get; set; }
}
