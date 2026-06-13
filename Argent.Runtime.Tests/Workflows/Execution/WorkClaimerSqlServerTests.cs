using Argent.Models.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Execution;

/// <summary>
/// Exercises <see cref="WorkClaimer"/> against a real SQL Server. This is the regression net
/// for the table-name bug (the query targeted <c>WorkItem</c> instead of <c>WorkItems</c>),
/// which the SQLite suite could never catch because it claims work via EF rather than the
/// raw-T-SQL claimer.
/// </summary>
[Collection("SqlServer")]
[Trait("Category", "Sql")]
public class WorkClaimerSqlServerTests
{
    private readonly SqlServerFixture _fx;

    public WorkClaimerSqlServerTests(SqlServerFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Claims_pending_work_item_and_flips_it_to_running()
    {
        Skip.IfNot(_fx.Available, "Docker / SQL Server not available in this environment");

        var instanceId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();

        await using (var db = _fx.CreateContext())
        {
            db.WorkItems.Add(new WorkItem
            {
                Id = workItemId,
                TokenId = tokenId,
                WorkflowInstanceId = instanceId,
                DefinitionId = Guid.NewGuid(),
                NodeId = Guid.NewGuid(),
                NodeType = "StartEvent",
                State = WorkItemState.Pending,
                TokenPayload = "{}",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var claimer = new WorkClaimer(_fx.ConnectionString);

        var claimed = await claimer.ClaimAsync(50, default);

        var mine = claimed.FirstOrDefault(c => c.WorkItemId == workItemId);
        Assert.NotNull(mine);
        Assert.Equal(tokenId, mine!.TokenId);
        Assert.Equal(instanceId, mine.InstanceId);
        Assert.Equal("StartEvent", mine.NodeType);

        await using (var check = _fx.CreateContext())
        {
            var wi = await check.WorkItems.FindAsync(workItemId);
            Assert.Equal(WorkItemState.Running, wi!.State);
            Assert.False(string.IsNullOrEmpty(wi.LockedBy));
            Assert.NotNull(wi.LockExpirationUtc);
        }

        // A second claim must not hand out the same (now Running) item again.
        var second = await claimer.ClaimAsync(50, default);
        Assert.DoesNotContain(second, c => c.WorkItemId == workItemId);
    }
}
