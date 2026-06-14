using Argent.Infrastructure.Data;
using Argent.Models.Enums;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
using Argent.Models.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Execution;

[Trait("Category", "Integration")]
public class WorkflowTraversalIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task LinearFlow_StartToEnd_CompletesInstance()
    {
        var start = new StartEvent { Id = Guid.NewGuid(), Name = "Start" };
        var activity = new JintActivity { Id = Guid.NewGuid(), Name = "Process" };
        var end = new EndEvent { Id = Guid.NewGuid(), Name = "End" };

        var definition = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata { CreatedAt = DateTime.UtcNow, CreatedBy = "test", Version = new Version(1, 0) },
            Nodes = [start, activity, end],
            Connections =
            [
                new Connection { From = start, To = activity },
                new Connection { From = activity, To = end },
            ],
        };

        var seed = await SeedWorkflowAsync(definition);

        // Verify seed data persisted
        await using (var check = CreateContext())
        {
            var items = await check.WorkItems.Where(wi => wi.WorkflowInstanceId == seed.InstanceId).ToListAsync();
            Assert.True(items.Count > 0, $"Expected at least 1 WorkItem after seed, found {items.Count}");
        }

        var runner = CreateRunner();

        // Verify still there after CreateRunnerAsync
        await using (var check2 = CreateContext())
        {
            var items2 = await check2.WorkItems.Where(wi => wi.WorkflowInstanceId == seed.InstanceId).ToListAsync();
            Assert.True(items2.Count > 0, $"Expected at least 1 WorkItem after CreateRunnerAsync, found {items2.Count}");
        }

        // Step 1: StartEvent -> activity
        var (token1, wi1, created1) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.NotNull(token1);
        Assert.Equal(TokenState.Consumed, token1!.State);
        Assert.NotNull(wi1);
        Assert.Equal(WorkItemState.Completed, wi1!.State);

        var actWorkItem = created1.FirstOrDefault(w => w.NodeType == nameof(JintActivity));
        Assert.NotNull(actWorkItem);
        Assert.Equal(WorkItemState.Pending, actWorkItem.State);

        // Step 2: activity -> EndEvent
        var (token2, wi2, created2) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.NotNull(token2);
        Assert.Equal(TokenState.Consumed, token2!.State);
        Assert.NotNull(wi2);
        Assert.Equal(WorkItemState.Completed, wi2!.State);

        var endWorkItem = created2.FirstOrDefault(w => w.NodeType == nameof(EndEvent));
        Assert.NotNull(endWorkItem);
        Assert.Equal(WorkItemState.Pending, endWorkItem.State);

        // Step 3: EndEvent - no outbound connections, instance completes
        var (token3, wi3, created3) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.NotNull(token3);
        Assert.Equal(TokenState.Consumed, token3!.State);
        Assert.NotNull(wi3);
        Assert.Equal(WorkItemState.Completed, wi3!.State);
        Assert.Empty(created3);

        var state = await GetInstanceStateAsync(seed.InstanceId);
        Assert.Equal(InstanceState.Completed, state);
    }

    [Fact]
    public async Task Instance_stays_pinned_to_its_start_version_after_newer_deploy()
    {
        var workflowId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var versionId1 = Guid.NewGuid();
        var versionId2 = Guid.NewGuid();

        // v1 — the version the instance starts on.
        var s1 = new StartEvent { Id = Guid.NewGuid(), Name = "Start v1" };
        var e1 = new EndEvent { Id = Guid.NewGuid(), Name = "End v1" };
        var def1 = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata { CreatedAt = DateTime.UtcNow, CreatedBy = "test", Version = new Version(1, 0) },
            Nodes = [s1, e1],
            Connections = [new Connection { From = s1, To = e1 }],
        };

        // v2 — a newer deploy with entirely different node ids. If the engine resolved
        // "latest deployed" it would load this and fail to find the instance's current node.
        var s2 = new StartEvent { Id = Guid.NewGuid(), Name = "Start v2" };
        var e2 = new EndEvent { Id = Guid.NewGuid(), Name = "End v2" };
        var def2 = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata { CreatedAt = DateTime.UtcNow, CreatedBy = "test", Version = new Version(2, 0) },
            Nodes = [s2, e2],
            Connections = [new Connection { From = s2, To = e2 }],
        };

        var tokenId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();

        await using (var db = CreateContext())
        {
            var workflow = new Workflow
            {
                Id = workflowId, Name = "Versioned", Description = "",
                CreatedOn = DateTime.UtcNow, UpdatedOn = DateTime.UtcNow, Tags = [],
            };
            db.WorkflowDefinitions.Add(workflow);

            // v1 has since been un-deployed by the v2 deploy.
            db.WorkflowVersions.Add(new WorkflowVersion
            {
                Id = versionId1, WorkflowId = workflowId, Workflow = workflow, Definition = def1,
                State = WorkflowDefinitionState.Published, Version = new Version(1, 0),
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            });
            db.WorkflowVersions.Add(new WorkflowVersion
            {
                Id = versionId2, WorkflowId = workflowId, Definition = def2,
                State = WorkflowDefinitionState.Deployed, Version = new Version(2, 0),
                CreatedAt = DateTime.UtcNow,
            });
            db.WorkflowInstances.Add(new WorkflowInstance
            {
                InstanceId = instanceId, WorkflowId = workflowId, VersionId = versionId1,
                State = InstanceState.Running, StartTime = DateTime.UtcNow,
            });
            db.WorkflowTokens.Add(new WorkflowToken
            {
                Id = tokenId, InstanceId = instanceId, NodeId = s1.Id,
                State = TokenState.Ready, Payload = "{}", CreatedAt = DateTime.UtcNow,
            });
            db.WorkItems.Add(new WorkItem
            {
                Id = workItemId, TokenId = tokenId, WorkflowInstanceId = instanceId,
                DefinitionId = workflowId, NodeId = s1.Id, NodeType = nameof(StartEvent),
                State = WorkItemState.Pending, TokenPayload = "{}", CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var runner = CreateRunner();
        var (_, wi, created) = await AdvanceAsync(runner, instanceId, workflowId);

        // The node was found and processed — i.e. v1 was used, not the newer v2.
        Assert.NotNull(wi);
        Assert.Equal(WorkItemState.Completed, wi!.State);
        Assert.Contains(created, w => w.NodeId == e1.Id);       // routed along v1's edge
        Assert.DoesNotContain(created, w => w.NodeId == e2.Id); // never touched v2
    }

    [Fact]
    public async Task ExclusiveGateway_ConditionMet_TakesMatchingPath()
    {
        var start = new StartEvent { Id = Guid.NewGuid(), Name = "Start" };
        var gw = new ExclusiveGateway { Id = Guid.NewGuid(), Name = "Choose" };
        var endA = new EndEvent { Id = Guid.NewGuid(), Name = "EndHigh" };
        var endB = new EndEvent { Id = Guid.NewGuid(), Name = "EndLow" };

        var definition = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata { CreatedAt = DateTime.UtcNow, CreatedBy = "test", Version = new Version(1, 0) },
            Nodes = [start, gw, endA, endB],
            Connections =
            [
                new Connection { From = start, To = gw },
                new Connection { From = gw, To = endA, Expression = "x > 5" },
                new Connection { From = gw, To = endB, Expression = "x <= 5" },
            ],
        };

        var seed = await SeedWorkflowAsync(definition, new Dictionary<string, object?> { ["x"] = 10 });
        var runner = CreateRunner();

        await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);

        var created = (await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId)).Created;

        var gwWorkItem = created.FirstOrDefault(w => w.NodeType == nameof(EndEvent));
        Assert.NotNull(gwWorkItem);

        await using var db = CreateContext();
        var endAToken = await db.WorkflowTokens
            .FirstOrDefaultAsync(t => t.InstanceId == seed.InstanceId && t.NodeId == endA.Id);
        Assert.NotNull(endAToken);
        Assert.Equal(TokenState.Ready, endAToken.State);

        var endBToken = await db.WorkflowTokens
            .FirstOrDefaultAsync(t => t.InstanceId == seed.InstanceId && t.NodeId == endB.Id);
        Assert.Null(endBToken);
    }

    [Fact]
    public async Task ExclusiveGateway_DefaultPath_WhenConditionNotMet()
    {
        var start = new StartEvent { Id = Guid.NewGuid(), Name = "Start" };
        var gw = new ExclusiveGateway { Id = Guid.NewGuid(), Name = "Choose" };
        var endA = new EndEvent { Id = Guid.NewGuid(), Name = "EndHigh" };
        var endB = new EndEvent { Id = Guid.NewGuid(), Name = "EndDefault" };

        var definition = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata { CreatedAt = DateTime.UtcNow, CreatedBy = "test", Version = new Version(1, 0) },
            Nodes = [start, gw, endA, endB],
            Connections =
            [
                new Connection { From = start, To = gw },
                new Connection { From = gw, To = endA, Expression = "x > 10" },
                new Connection { From = gw, To = endB },
            ],
        };

        var seed = await SeedWorkflowAsync(definition, new Dictionary<string, object?> { ["x"] = 5 });
        var runner = CreateRunner();

        await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        var created = (await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId)).Created;

        var endBWorkItem = created.FirstOrDefault(w => w.NodeId == endB.Id);
        Assert.NotNull(endBWorkItem);

        await using var db = CreateContext();
        var endAToken = await db.WorkflowTokens
            .FirstOrDefaultAsync(t => t.InstanceId == seed.InstanceId && t.NodeId == endA.Id);
        Assert.Null(endAToken);
    }

    [Fact]
    public async Task ExclusiveGateway_NoMatch_FailsWorkItem()
    {
        var start = new StartEvent { Id = Guid.NewGuid(), Name = "Start" };
        var gw = new ExclusiveGateway { Id = Guid.NewGuid(), Name = "Choose" };
        var endA = new EndEvent { Id = Guid.NewGuid(), Name = "End" };

        var definition = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata { CreatedAt = DateTime.UtcNow, CreatedBy = "test", Version = new Version(1, 0) },
            Nodes = [start, gw, endA],
            Connections =
            [
                new Connection { From = start, To = gw },
                new Connection { From = gw, To = endA, Expression = "x > 10" },
            ],
        };

        var seed = await SeedWorkflowAsync(definition, new Dictionary<string, object?> { ["x"] = 5 });
        var runner = CreateRunner();

        await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        var (_, gwWorkItem, _) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);

        Assert.NotNull(gwWorkItem);
        Assert.Equal(WorkItemState.Failed, gwWorkItem!.State);
    }

    [Fact]
    public async Task ParallelGateway_SplitAndJoin_AllPathsExecuted()
    {
        var start = new StartEvent { Id = Guid.NewGuid(), Name = "Start" };
        var split = new ParallelGateway { Id = Guid.NewGuid(), Name = "Split" };
        var actA = new JintActivity { Id = Guid.NewGuid(), Name = "BranchA" };
        var actB = new JintActivity { Id = Guid.NewGuid(), Name = "BranchB" };
        var join = new ParallelGateway { Id = Guid.NewGuid(), Name = "Join" };
        var end = new EndEvent { Id = Guid.NewGuid(), Name = "End" };

        var definition = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata { CreatedAt = DateTime.UtcNow, CreatedBy = "test", Version = new Version(1, 0) },
            Nodes = [start, split, actA, actB, join, end],
            Connections =
            [
                new Connection { From = start, To = split },
                new Connection { From = split, To = actA },
                new Connection { From = split, To = actB },
                new Connection { From = actA, To = join },
                new Connection { From = actB, To = join },
                new Connection { From = join, To = end },
            ],
        };

        var seed = await SeedWorkflowAsync(definition);
        var runner = CreateRunner();

        // Step 1: StartEvent -> split gateway
        await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);

        // Step 2: Split gateway creates 2 tokens
        var (_, _, created) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.Equal(2, created.Count);
        Assert.Contains(created, w => w.NodeId == actA.Id);
        Assert.Contains(created, w => w.NodeId == actB.Id);

        // Step 3: Process actA -> moves to join
        var (tokenA, wiA, _) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.NotNull(tokenA);
        Assert.Equal(TokenState.Consumed, tokenA!.State);

        // Step 4: Process actB -> moves to join
        var (tokenB, wiB, _) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.NotNull(tokenB);
        Assert.Equal(TokenState.Consumed, tokenB!.State);

        // Step 5: First token arrives at join -> consumed, waiting for sibling
        var (firstJoinToken, firstJoinWi, firstJoinCreated) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.NotNull(firstJoinWi);
        Assert.Equal(WorkItemState.Completed, firstJoinWi!.State);
        Assert.Empty(firstJoinCreated);

        var joinTokens = await GetTokensAsync(seed.InstanceId);
        var consumedAtJoin = joinTokens.Count(t => t.NodeId == join.Id && t.State == TokenState.Consumed);
        Assert.Equal(1, consumedAtJoin);

        // Step 6: Second token arrives at join -> all arrived, fires join, creates EndEvent token
        var (secondJoinToken, secondJoinWi, joinCreated) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.NotNull(secondJoinWi);
        Assert.Equal(WorkItemState.Completed, secondJoinWi!.State);

        var endWorkItem = joinCreated.FirstOrDefault(w => w.NodeId == end.Id);
        Assert.NotNull(endWorkItem);

        // Step 7: EndEvent completes the instance
        var (endToken, endWi, _) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.NotNull(endToken);
        Assert.Equal(TokenState.Consumed, endToken!.State);
        Assert.NotNull(endWi);
        Assert.Equal(WorkItemState.Completed, endWi!.State);

        var state = await GetInstanceStateAsync(seed.InstanceId);
        Assert.Equal(InstanceState.Completed, state);
    }

    [Fact]
    public async Task InclusiveGateway_MultipleConditions_BothPathsActivated()
    {
        var start = new StartEvent { Id = Guid.NewGuid(), Name = "Start" };
        var gw = new InclusiveGateway { Id = Guid.NewGuid(), Name = "Choose" };
        var endA = new EndEvent { Id = Guid.NewGuid(), Name = "EndA" };
        var endB = new EndEvent { Id = Guid.NewGuid(), Name = "EndB" };
        var join = new InclusiveGateway { Id = Guid.NewGuid(), Name = "Join" };
        var final = new EndEvent { Id = Guid.NewGuid(), Name = "Final" };

        var definition = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata { CreatedAt = DateTime.UtcNow, CreatedBy = "test", Version = new Version(1, 0) },
            Nodes = [start, gw, endA, endB, join, final],
            Connections =
            [
                new Connection { From = start, To = gw },
                new Connection { From = gw, To = endA, Expression = "x > 5" },
                new Connection { From = gw, To = endB, Expression = "x < 10" },
                new Connection { From = endA, To = join },
                new Connection { From = endB, To = join },
                new Connection { From = join, To = final },
            ],
        };

        var seed = await SeedWorkflowAsync(definition, new Dictionary<string, object?> { ["x"] = 7 });
        var runner = CreateRunner();

        // Step 1: StartEvent -> inclusive gateway
        await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);

        // Step 2: Both conditions match (x=7: x > 5 AND x < 10)
        var (_, _, created) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.Equal(2, created.Count);
        Assert.Contains(created, w => w.NodeId == endA.Id);
        Assert.Contains(created, w => w.NodeId == endB.Id);

        // Steps 3 & 4: Process both end events to reach the join
        await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);

        // Step 5: First arrives at join -> consumed, waiting
        var (firstJoinT, _, firstJoinC) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.Empty(firstJoinC);

        // Step 6: Second arrives at join -> join fires
        var (secondJoinT, _, joinC) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        var finalWorkItem = joinC.FirstOrDefault(w => w.NodeId == final.Id);
        Assert.NotNull(finalWorkItem);

        // Step 7: Final end event completes
        await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        var state = await GetInstanceStateAsync(seed.InstanceId);
        Assert.Equal(InstanceState.Completed, state);
    }

    [Fact]
    public async Task InclusiveGateway_SingleCondition_OnlyMatchingPathActivated()
    {
        var start = new StartEvent { Id = Guid.NewGuid(), Name = "Start" };
        var gw = new InclusiveGateway { Id = Guid.NewGuid(), Name = "Choose" };
        var endA = new EndEvent { Id = Guid.NewGuid(), Name = "EndHigh" };
        var endB = new EndEvent { Id = Guid.NewGuid(), Name = "EndLow" };

        var definition = new WorkflowDefinition
        {
            Metadata = new WorkflowMetadata { CreatedAt = DateTime.UtcNow, CreatedBy = "test", Version = new Version(1, 0) },
            Nodes = [start, gw, endA, endB],
            Connections =
            [
                new Connection { From = start, To = gw },
                new Connection { From = gw, To = endA, Expression = "x > 10" },
                new Connection { From = gw, To = endB, Expression = "x < 10" },
            ],
        };

        var seed = await SeedWorkflowAsync(definition, new Dictionary<string, object?> { ["x"] = 3 });
        var runner = CreateRunner();

        await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);

        var (_, _, created) = await AdvanceAsync(runner, seed.InstanceId, seed.WorkflowId);
        Assert.Single(created);
        Assert.Contains(created, w => w.NodeId == endB.Id);
    }
}
