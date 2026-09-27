using Argent.Core.Workers;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.Auditing;
using Argent.Core.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Runtime.Workers;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Execution;

/// <summary>
/// Drives a token through the whole enqueue-and-wait cycle against the SQLite harness: the engine
/// parks at a worker node, a worker completes the request, and the token resumes with the worker's
/// outputs as process variables. This is the behaviour the worker feature exists for, so it is
/// tested as a flow rather than as isolated units.
/// </summary>
[Trait("Category", "Integration")]
public class WorkerActivityIntegrationTests : IntegrationTestBase
{
    private WorkerRequestQueue _queue = null!;
    private WorkerRegistry _registry = null!;
    private TokenRunner _runner = null!;

    public WorkerActivityIntegrationTests()
    {
        var factory = new Mock<IDbContextFactory<ArgentDbContext>>();
        factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateContext());

        _queue = new WorkerRequestQueue(
            factory.Object,
            "DataSource=:memory:",
            Mock.Of<ILogger<WorkerRequestQueue>>());

        _registry = new WorkerRegistry(
            factory.Object,
            Mock.Of<ILogger<WorkerRegistry>>());
    }

    private static WorkflowDefinition Definition(out WorkerActivity workerNode, out EndEvent endNode)
    {
        var start = new StartEvent { Id = Guid.NewGuid(), Name = "Start" };
        workerNode = new WorkerActivity
        {
            Id = Guid.NewGuid(),
            Name = "Render report",
            WorkerName = "reports",
            Subject = "render",
            Parameters = [new WorkerParameter { Key = "customer", Value = "{{CustomerName}}" }],
            TimeoutSeconds = 300,
            MaxAttempts = 2
        };
        endNode = new EndEvent { Id = Guid.NewGuid(), Name = "End" };

        return new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata
            {
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test",
                Version = new Version(1, 0)
            },
            Nodes = [start, workerNode, endNode],
            Connections =
            [
                new Connection { From = start, To = workerNode },
                new Connection { From = workerNode, To = endNode },
            ]
        };
    }

    private TokenRunner CreateWorkerRunner()
    {
        var services = new ServiceCollection();

        services.AddScoped<ArgentDbContext>(_ => CreateContext());
        services.AddScoped<ITokenMovement, TokenMovement>();
        services.AddScoped<IEnumerable<INodeHandler>>(_ =>
        [
            new StartEventHandler(),
            new EndEventHandler(),
            new WorkerActivityHandler(_queue, _registry),
        ]);

        var factory = new Mock<IDbContextFactory<ArgentDbContext>>();
        factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateContext());
        services.AddSingleton<IDbContextFactory<ArgentDbContext>>(factory.Object);
        services.AddSingleton<ILogger<TokenRunner>>(Mock.Of<ILogger<TokenRunner>>());

        var provider = services.BuildServiceProvider();

        return new TokenRunner(
            provider.GetRequiredService<IServiceScopeFactory>(),
            null!,
            provider.GetRequiredService<IDbContextFactory<ArgentDbContext>>(),
            new TimerManager(
                provider.GetRequiredService<IDbContextFactory<ArgentDbContext>>(),
                Mock.Of<ILogger<TimerManager>>()),
            provider.GetRequiredService<ILogger<TokenRunner>>());
    }

    [Fact]
    public async Task Token_parks_at_the_worker_node_until_the_worker_responds()
    {
        var definition = Definition(out var workerNode, out var endNode);

        var seed = await SeedWorkflowAsync(
            definition,
            new Dictionary<string, object?> { ["CustomerName"] = "Contoso" });

        await _registry.ProvisionAsync(new Worker { Name = "reports" });

        _runner = CreateWorkerRunner();

        // Start -> worker node
        var (_, _, created) = await AdvanceAsync(_runner, seed.InstanceId, seed.WorkflowId);
        var nodeItem = created.First(w => w.NodeType == nameof(WorkerActivity));
        Assert.Equal(WorkItemState.Pending, nodeItem.State);

        // The node enqueues and parks.
        var (_, waiting, _) = await AdvanceAsync(_runner, seed.InstanceId, seed.WorkflowId);
        Assert.Equal(WorkItemState.Waiting, waiting!.State);

        await using var check = CreateContext();
        var request = await check.WorkerRequests.SingleAsync();
        Assert.Equal("reports", request.WorkerName);
        Assert.Equal("render", request.Subject);
        Assert.Equal(seed.InstanceId, request.InstanceId);
        Assert.Equal(workerNode.Id, request.NodeId);
        Assert.Equal(nodeItem.Id, request.WorkItemId);
        Assert.Equal("Contoso", request.GetParameters()["customer"]);
        Assert.Equal(300, request.TimeoutSeconds);
        Assert.Equal(2, request.MaxAttempts);

        // A second pass over the node while the worker is silent must not enqueue a duplicate.
        await using (var db2 = CreateContext())
        {
            var again = await db2.WorkerRequests.FindAsync(request.Id);
            again!.State = WorkerRequestState.Pending;
            await db2.SaveChangesAsync();
            var item = await db2.WorkItems.FindAsync(nodeItem.Id);
            item!.State = WorkItemState.Pending;
            await db2.SaveChangesAsync();
        }

        await RunItemAsync(nodeItem.Id, seed.InstanceId);

        await using var afterRepeat = CreateContext();
        Assert.Equal(1, await afterRepeat.WorkerRequests.CountAsync());

        // The worker responds.
        await CompleteAsWorkerAsync(request.Id, new Dictionary<string, string>
        {
            ["reportUrl"] = "https://files/report.pdf",
            ["pageCount"] = "12"
        });

        // The node completes and moves the token on, carrying the outputs as process variables.
        var (_, completed, createdAfter) = await AdvanceAsync(_runner, seed.InstanceId, seed.WorkflowId);
        Assert.Equal(WorkItemState.Completed, completed!.State);
        Assert.Contains(createdAfter, w => w.NodeType == nameof(EndEvent));

        var token = await GetTokensAsync(seed.InstanceId);
        var nextToken = token.Single(t => t.NodeId == endNode.Id);
        var variables = TokenMovement.DeserializePayload(nextToken.Payload);
        Assert.Equal("https://files/report.pdf", variables["reportUrl"]);
        Assert.Equal("12", variables["pageCount"]);
    }

    [Fact]
    public async Task A_failed_worker_response_fails_the_node_and_journals_it()
    {
        var definition = Definition(out _, out _);
        var seed = await SeedWorkflowAsync(definition);

        await _registry.ProvisionAsync(new Worker { Name = "reports" });

        _runner = CreateWorkerRunner();

        await AdvanceAsync(_runner, seed.InstanceId, seed.WorkflowId);
        var (_, waiting, _) = await AdvanceAsync(_runner, seed.InstanceId, seed.WorkflowId);
        Assert.Equal(WorkItemState.Waiting, waiting!.State);

        await using (var db = CreateContext())
        {
            var request = await db.WorkerRequests.SingleAsync();
            request.State = WorkerRequestState.Claimed;
            request.ClaimedByWorkerId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            var request = await db.WorkerRequests.SingleAsync();
            await _queue.CompleteAsync(request.Id, request.ClaimedByWorkerId!.Value, false, null,
                "the upstream API returned 500");
        }

        var (_, failed, _) = await AdvanceAsync(_runner, seed.InstanceId, seed.WorkflowId);
        Assert.Equal(WorkItemState.Failed, failed!.State);

        await using var check = CreateContext();
        var journal = await check.WorkflowJournalEntries
            .Where(j => j.InstanceId == seed.InstanceId)
            .ToListAsync();

        var failure = Assert.Single(journal.Where(j => j.EventType == nameof(WorkflowAuditEventType.NodeFailed)));
        Assert.Contains("the upstream API returned 500", failure.Details);
    }

    [Fact]
    public async Task A_node_addressing_an_unknown_worker_fails_with_a_readable_message()
    {
        var definition = Definition(out _, out _);
        definition.Nodes.OfType<WorkerActivity>().Single().WorkerName = "does-not-exist";

        var seed = await SeedWorkflowAsync(definition);
        _runner = CreateWorkerRunner();

        await AdvanceAsync(_runner, seed.InstanceId, seed.WorkflowId);
        var (_, failed, _) = await AdvanceAsync(_runner, seed.InstanceId, seed.WorkflowId);

        Assert.Equal(WorkItemState.Failed, failed!.State);

        await using var check = CreateContext();
        var entry = await check.WorkflowJournalEntries.SingleAsync(j =>
            j.InstanceId == seed.InstanceId && j.EventType == nameof(WorkflowAuditEventType.NodeFailed));
        Assert.Contains("does-not-exist", entry.Details);

        // Nothing should have been enqueued for a worker that cannot exist.
        Assert.Equal(0, await check.WorkerRequests.CountAsync());
    }

    /// <summary>Runs one already-pending work item through the runner.</summary>
    private async Task RunItemAsync(Guid workItemId, Guid instanceId)
    {
        await using var db = CreateContext();
        var item = await db.WorkItems.FindAsync(workItemId);
        item!.State = WorkItemState.Running;
        await db.SaveChangesAsync();

        var tokenId = item.TokenId;
        var nodeId = item.NodeId;

        await _runner.RunAsync(
            new ClaimedWork(workItemId, tokenId, nodeId, nameof(WorkerActivity), 0, 3),
            default);

        await using var check = CreateContext();
        Assert.Equal(instanceId, (await check.WorkflowTokens.FindAsync(tokenId))!.InstanceId);
    }

    /// <summary>Completes the outstanding request as the worker holding it would through the API.</summary>
    private async Task CompleteAsWorkerAsync(Guid requestId, Dictionary<string, string> outputs)
    {
        await using var db = CreateContext();
        var request = await db.WorkerRequests.FindAsync(requestId);
        request!.State = WorkerRequestState.Claimed;
        request.ClaimedByWorkerId = Guid.NewGuid();
        request.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(1);
        await db.SaveChangesAsync();

        await _queue.CompleteAsync(requestId, request.ClaimedByWorkerId!.Value, true, outputs, null);
    }
}
