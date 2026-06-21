using Argent.Contracts.Workflows;
using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Enums;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Execution;

public class TokenRunnerTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Mock<ILogger<TokenRunner>> _loggerMock = new();
    private readonly Mock<IDbContextFactory<ArgentDbContext>> _ctxFactoryMock = new();
    private readonly Mock<ITokenMovement> _movementMock = new();

    private ServiceProvider? _serviceProvider;

    [Fact]
    public async Task Consumed_token_completes_work_item_without_processing()
    {
        var workflowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();

        var (runner, db) = CreateRunner(workflowId, nodeId);

        db.WorkflowTokens.Add(new WorkflowToken
        {
            Id = tokenId, InstanceId = instanceId, NodeId = nodeId,
            State = TokenState.Consumed, CreatedAt = DateTime.UtcNow,
        });
        db.WorkItems.Add(new WorkItem
        {
            Id = workItemId, TokenId = tokenId,
            NodeId = nodeId, NodeType = "StartEvent",
            State = WorkItemState.Running, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var claimed = new ClaimedWork(workItemId, tokenId, nodeId, "StartEvent", 0, 3);

        // Verify DI context sees the seed data
        using (var scope = _serviceProvider!.CreateScope())
        {
            var diDb = scope.ServiceProvider.GetRequiredService<ArgentDbContext>();
            var token = await diDb.WorkflowTokens.FindAsync(tokenId);
            var version = await diDb.WorkflowVersions
                .Where(v => v.WorkflowId == workflowId && v.State == Argent.Models.Enums.WorkflowDefinitionState.Deployed)
                .FirstOrDefaultAsync();
            Assert.NotNull(token); // DI context must see data
            Assert.NotNull(version);
        }

        await runner.RunAsync(claimed, default);

        await using var v = CreateContext();
        var wi = await v.WorkItems.FindAsync(workItemId);
        Assert.Equal(WorkItemState.Completed, wi!.State);
        _movementMock.Verify(m => m.CommitAsync(It.IsAny<TokenMovementRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task No_deployed_definition_fails_work_item()
    {
        var workflowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();

        // Seed a version for a different workflow so the lookup returns nothing
        var (runner, db) = CreateRunner(Guid.NewGuid(), nodeId);

        db.WorkflowTokens.Add(new WorkflowToken
        {
            Id = tokenId, InstanceId = instanceId, NodeId = nodeId,
            State = TokenState.Ready, CreatedAt = DateTime.UtcNow,
        });
        db.WorkItems.Add(new WorkItem
        {
            Id = workItemId, TokenId = tokenId,
            NodeId = nodeId, NodeType = "StartEvent",
            State = WorkItemState.Running, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var claimed = new ClaimedWork(workItemId, tokenId, nodeId, "StartEvent", 0, 3);

        await runner.RunAsync(claimed, default);

        await using var v = CreateContext();
        var wi = await v.WorkItems.FindAsync(workItemId);
        Assert.Equal(WorkItemState.Failed, wi!.State);
    }

    [Fact]
    public async Task Handle_success_calls_CommitAsync_and_completes()
    {
        var workflowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var nextNodeId = Guid.NewGuid();

        var (runner, db) = CreateRunner(workflowId, nodeId, nextNodeId);

        db.WorkflowInstances.Add(new WorkflowInstance
        {
            InstanceId = instanceId,
            WorkflowId = workflowId,
            State = InstanceState.Running,
            StartTime = DateTime.UtcNow,
        });
        db.WorkflowTokens.Add(new WorkflowToken
        {
            Id = tokenId, InstanceId = instanceId, NodeId = nodeId,
            State = TokenState.Ready, CreatedAt = DateTime.UtcNow,
        });
        db.WorkItems.Add(new WorkItem
        {
            Id = workItemId, TokenId = tokenId,
            NodeId = nodeId, NodeType = "StartEvent",
            State = WorkItemState.Running, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        _movementMock
            .Setup(m => m.CommitAsync(It.IsAny<TokenMovementRequest>(), default))
            .Returns(Task.CompletedTask);

        var claimed = new ClaimedWork(workItemId, tokenId, nodeId, "StartEvent", 0, 3);

        await runner.RunAsync(claimed, default);

        await using var v = CreateContext();
        var wi = await v.WorkItems.FindAsync(workItemId);
        Assert.Equal(WorkItemState.Completed, wi!.State);
        _movementMock.Verify(m => m.CommitAsync(
            It.Is<TokenMovementRequest>(r => r.ConsumedTokenId == tokenId),
            default), Times.Once);
    }

    [Fact]
    public async Task Handler_returns_Waiting_sets_work_item_state()
    {
        var workflowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();

        var handlerMock = new Mock<INodeHandler>();
        handlerMock.Setup(h => h.HandledNodeType).Returns(typeof(StartEvent));
        handlerMock
            .Setup(h => h.ExecuteAsync(It.IsAny<NodeBase>(), It.IsAny<ITokenExecutionContext>(), default))
            .ReturnsAsync(new NodeResult(true, ResultType: NodeResultType.Waiting));

        var (runner, db) = CreateRunner(workflowId, nodeId, handler: handlerMock.Object);

        db.WorkflowInstances.Add(new WorkflowInstance
        {
            InstanceId = instanceId,
            WorkflowId = workflowId,
            State = InstanceState.Running,
            StartTime = DateTime.UtcNow,
        });
        db.WorkflowTokens.Add(new WorkflowToken
        {
            Id = tokenId, InstanceId = instanceId, NodeId = nodeId,
            State = TokenState.Ready, CreatedAt = DateTime.UtcNow,
        });
        db.WorkItems.Add(new WorkItem
        {
            Id = workItemId, TokenId = tokenId,
            NodeId = nodeId, NodeType = "StartEvent",
            State = WorkItemState.Running, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var claimed = new ClaimedWork(workItemId, tokenId, nodeId, "StartEvent", 0, 3);

        await runner.RunAsync(claimed, default);

        await using var v = CreateContext();
        var wi = await v.WorkItems.FindAsync(workItemId);
        Assert.Equal(WorkItemState.Waiting, wi!.State);
        _movementMock.Verify(m => m.CommitAsync(It.IsAny<TokenMovementRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task Exception_in_handler_triggers_failure_retry()
    {
        var workflowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();

        var handlerMock = new Mock<INodeHandler>();
        handlerMock.Setup(h => h.HandledNodeType).Returns(typeof(StartEvent));
        handlerMock
            .Setup(h => h.ExecuteAsync(It.IsAny<NodeBase>(), It.IsAny<ITokenExecutionContext>(), default))
            .ThrowsAsync(new InvalidOperationException("test error"));

        var (runner, db) = CreateRunner(workflowId, nodeId, handler: handlerMock.Object);

        db.WorkflowInstances.Add(new WorkflowInstance
        {
            InstanceId = instanceId,
            WorkflowId = workflowId,
            State = InstanceState.Running,
            StartTime = DateTime.UtcNow,
        });
        db.WorkflowTokens.Add(new WorkflowToken
        {
            Id = tokenId, InstanceId = instanceId, NodeId = nodeId,
            State = TokenState.Ready, CreatedAt = DateTime.UtcNow,
        });
        db.WorkItems.Add(new WorkItem
        {
            Id = workItemId, TokenId = tokenId,
            NodeId = nodeId, NodeType = "StartEvent",
            State = WorkItemState.Running, CreatedAt = DateTime.UtcNow,
            RetryCount = 0, MaxRetries = 3,
        });
        await db.SaveChangesAsync();

        var claimed = new ClaimedWork(workItemId, tokenId, nodeId, "StartEvent", 0, 3);

        await runner.RunAsync(claimed, default);

        await using var v = CreateContext();
        var wi = await v.WorkItems.FindAsync(workItemId);
        Assert.Equal(WorkItemState.Pending, wi!.State);
        Assert.Equal(1, wi.RetryCount);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        using var ctx = CreateContext();
        ctx.Database.EnsureDeleted();
    }

    private ArgentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new ArgentDbContext(options);
    }

    private (TokenRunner runner, ArgentDbContext db) CreateRunner(
        Guid workflowId,
        Guid nodeId,
        Guid? nextNodeId = null,
        INodeHandler? handler = null)
    {
        var db = CreateContext();

        var thisNode = new StartEvent { Id = nodeId, Name = "TestNode" };
        var definition = new WorkflowDefinition
        {
            Nodes = [thisNode],
            Connections = [],
        };

        if (nextNodeId.HasValue)
        {
            var nextNode = new StartEvent { Id = nextNodeId.Value, Name = "NextNode" };
            definition.Nodes.Add(nextNode);
            definition.Connections.Add(new Connection
            {
                From = thisNode,
                To = nextNode,
            });
        }

        db.WorkflowVersions.Add(new WorkflowVersion
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            Definition = definition,
            State = WorkflowDefinitionState.Deployed,
            Version = new Version(1, 0),
            CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();

        var services = new ServiceCollection();
        services.AddScoped<ArgentDbContext>(_ => CreateContext());
        services.AddScoped<ITokenMovement>(_ => _movementMock.Object);

        if (handler != null)
        {
            services.AddScoped<IEnumerable<INodeHandler>>(_ => [handler]);
        }
        else
        {
            var defaultHandler = new Mock<INodeHandler>();
            defaultHandler.Setup(h => h.HandledNodeType).Returns(typeof(StartEvent));
            defaultHandler
                .Setup(h => h.ExecuteAsync(It.IsAny<NodeBase>(), It.IsAny<ITokenExecutionContext>(), default))
                .ReturnsAsync(new NodeResult(true, ExplicitTargetNodeIds: nextNodeId.HasValue ? new[] { nextNodeId.Value } : null));
            services.AddScoped<IEnumerable<INodeHandler>>(_ => [defaultHandler.Object]);
        }

        _serviceProvider = services.BuildServiceProvider();
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _ctxFactoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateContext());

        var timerManager = new TimerManager(_ctxFactoryMock.Object, Mock.Of<ILogger<TimerManager>>());
        var runner = new TestableTokenRunner(scopeFactory, null!, _ctxFactoryMock.Object, timerManager, _loggerMock.Object);
        return (runner, db);
    }

    /// <summary>
    /// TokenRunner subclass that replaces ExecuteUpdateAsync calls with
    /// InMemory-compatible SaveChangesAsync calls.
    /// </summary>
    private class TestableTokenRunner : TokenRunner
    {
        public TestableTokenRunner(
            IServiceScopeFactory scopeFactory,
            IWorkflowNodeRegistry nodeRegistry,
            IDbContextFactory<ArgentDbContext> contextFactory,
            TimerManager timerManager,
            ILogger<TokenRunner> logger)
            : base(scopeFactory, nodeRegistry, contextFactory, timerManager, logger) { }

        protected internal override async Task SetWorkItemStateCoreAsync(
            ArgentDbContext db, Guid workItemId, WorkItemState state, CancellationToken ct)
        {
            var wi = await db.WorkItems.FindAsync([workItemId], ct);
            if (wi != null) wi.State = state;
            await db.SaveChangesAsync(ct);
        }

        protected internal override async Task HandleFailureAsync(
            ArgentDbContext db, ClaimedWork claimed, string? errorMessage, CancellationToken ct)
        {
            var wi = await db.WorkItems.FindAsync([claimed.WorkItemId], ct);
            if (wi == null) return;

            if (wi.RetryCount < wi.MaxRetries)
            {
                wi.State = WorkItemState.Pending;
                wi.LockedBy = null;
                wi.LockExpirationUtc = null;
                wi.RetryCount++;
            }
            else
            {
                wi.State = WorkItemState.Failed;
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
