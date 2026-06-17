using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Enums;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
using Argent.Models.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Execution;

/// <summary>
/// Drives two sibling tokens into a parallel-gateway join concurrently against a real SQL
/// Server. This is the regression net for the join race: the merge logic must let exactly one
/// sibling fire the join — never zero (which stalls the instance) and never two (which
/// duplicates the downstream token).
/// </summary>
[Collection("SqlServer")]
[Trait("Category", "Sql")]
public class ParallelJoinConcurrencyTests
{
    private readonly SqlServerFixture _fx;

    public ParallelJoinConcurrencyTests(SqlServerFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Two_siblings_arriving_concurrently_fire_join_exactly_once()
    {
        Skip.IfNot(_fx.Available, "Docker / SQL Server not available in this environment");

        var split = new ParallelGateway { Id = Guid.NewGuid(), Name = "Split" };
        var a = new JintActivity { Id = Guid.NewGuid(), Name = "A" };
        var b = new JintActivity { Id = Guid.NewGuid(), Name = "B" };
        var join = new ParallelGateway { Id = Guid.NewGuid(), Name = "Join" };
        var end = new EndEvent { Id = Guid.NewGuid(), Name = "End" };

        var definition = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata { CreatedAt = DateTime.UtcNow, CreatedBy = "test", Version = new Version(1, 0) },
            Nodes = [split, a, b, join, end],
            Connections =
            [
                new Connection { From = split, To = a },
                new Connection { From = split, To = b },
                new Connection { From = a, To = join },
                new Connection { From = b, To = join },
                new Connection { From = join, To = end },
            ],
        };

        var workflowId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var token1Id = Guid.NewGuid();
        var token2Id = Guid.NewGuid();
        var wi1Id = Guid.NewGuid();
        var wi2Id = Guid.NewGuid();

        await using (var db = _fx.CreateContext())
        {
            var workflow = new Workflow
            {
                Id = workflowId,
                Name = "Join Race",
                Description = "",
                CreatedOn = DateTime.UtcNow,
                UpdatedOn = DateTime.UtcNow,
                Tags = [],
            };
            db.Workflows.Add(workflow);
            db.WorkflowVersions.Add(new WorkflowVersion
            {
                Id = versionId,
                WorkflowId = workflowId,
                Workflow = workflow,
                Definition = definition,
                State = WorkflowDefinitionState.Deployed,
                Version = new Version(1, 0),
                CreatedAt = DateTime.UtcNow,
            });
            db.WorkflowInstances.Add(new WorkflowInstance
            {
                InstanceId = instanceId,
                WorkflowId = workflowId,
                VersionId = versionId,
                State = InstanceState.Running,
                StartTime = DateTime.UtcNow,
            });

            // Both siblings already sit at the join, members of the same group of 2.
            foreach (var (tokenId, wiId) in new[] { (token1Id, wi1Id), (token2Id, wi2Id) })
            {
                db.WorkflowTokens.Add(new WorkflowToken
                {
                    Id = tokenId,
                    InstanceId = instanceId,
                    NodeId = join.Id,
                    State = TokenState.Ready,
                    Payload = "{}",
                    GroupId = groupId,
                    TokenCount = 2,
                    CreatedAt = DateTime.UtcNow,
                });
                db.WorkItems.Add(new WorkItem
                {
                    Id = wiId,
                    TokenId = tokenId,
                    NodeId = join.Id,
                    NodeType = nameof(ParallelGateway),
                    State = WorkItemState.Pending,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            await db.SaveChangesAsync();
        }

        var runner = CreateRunner();

        var claim1 = new ClaimedWork(wi1Id, token1Id, join.Id, nameof(ParallelGateway), 0, 3);
        var claim2 = new ClaimedWork(wi2Id, token2Id, join.Id, nameof(ParallelGateway), 0, 3);

        // Run both arrivals concurrently — the heart of the race.
        await Task.WhenAll(
            Task.Run(() => runner.RunAsync(claim1, default)),
            Task.Run(() => runner.RunAsync(claim2, default)));

        await using var check = _fx.CreateContext();

        var endTokens = await check.WorkflowTokens
            .CountAsync(t => t.InstanceId == instanceId && t.NodeId == end.Id);
        Assert.Equal(1, endTokens); // exactly one downstream token — not 0 (stalled), not 2 (double-fired)

        var endWorkItems = await check.WorkItems
            .CountAsync(w => check.WorkflowTokens.Any(t => t.Id == w.TokenId && t.InstanceId == instanceId) && w.NodeId == end.Id);
        Assert.Equal(1, endWorkItems);

        var joinConsumed = await check.WorkflowTokens
            .CountAsync(t => t.InstanceId == instanceId && t.NodeId == join.Id && t.State == TokenState.Consumed);
        Assert.Equal(2, joinConsumed); // both siblings consumed
    }

    private TokenRunner CreateRunner()
    {
        var services = new ServiceCollection();
        services.AddScoped<ArgentDbContext>(_ => _fx.CreateContext());
        services.AddScoped<ITokenMovement, TokenMovement>();
        services.AddScoped<IEnumerable<INodeHandler>>(_ =>
        [
            new StartEventHandler(),
            new EndEventHandler(),
            new ParallelGatewayEvaluator(),
        ]);

        var factoryMock = new Mock<IDbContextFactory<ArgentDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _fx.CreateContext());
        services.AddSingleton(factoryMock.Object);
        services.AddSingleton(Mock.Of<ILogger<TokenRunner>>());

        var provider = services.BuildServiceProvider();

        var contextFactory = provider.GetRequiredService<IDbContextFactory<ArgentDbContext>>();
        var timerManager = new TimerManager(contextFactory, Mock.Of<ILogger<TimerManager>>());
        return new TokenRunner(
            provider.GetRequiredService<IServiceScopeFactory>(),
            null!,
            contextFactory,
            timerManager,
            provider.GetRequiredService<ILogger<TokenRunner>>());
    }
}
