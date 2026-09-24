using System.Text.Json;
using Argent.Core.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Runtime.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Execution;

public sealed class TaskInboxServiceTests
{
    [Fact]
    public async Task Query_returns_only_accessible_tasks_in_requested_order_and_page()
    {
        var userId = Guid.NewGuid().ToString();
        await using var fixture = await Fixture.CreateAsync(
            Task("Later", userId, priority: 1, due: DateTime.UtcNow.AddDays(2)),
            Task("Soon", userId, priority: 2, due: DateTime.UtcNow.AddHours(2)),
            Task("Assigned", null, assignedTo: userId, due: DateTime.UtcNow.AddDays(1)),
            Task("Someone else's", Guid.NewGuid().ToString(), due: DateTime.UtcNow.AddMinutes(5)));

        var result = await fixture.Service.QueryForUserAsync(userId, [], new TaskListRequest
        {
            Sort = TaskListSort.DueDate,
            Page = 1,
            PageSize = 2
        });

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Soon", result.Items[0].Title);
        Assert.True(result.Items[0].CanClaim);
        Assert.Equal("Assigned", result.Items[1].Title);
        Assert.True(result.Items[1].CanRelease);
    }

    [Fact]
    public async Task Query_applies_search_and_completed_scope()
    {
        var userId = Guid.NewGuid().ToString();
        var completed = Task("Review permit", userId);
        completed.State = UserTaskState.Completed;
        await using var fixture = await Fixture.CreateAsync(completed, Task("Review invoice", userId));

        var result = await fixture.Service.QueryForUserAsync(userId, [], new TaskListRequest
        {
            Search = "permit",
            Scope = TaskListScope.Completed
        });

        var item = Assert.Single(result.Items);
        Assert.Equal(completed.Id, item.Id);
        Assert.False(item.CanClaim);
        Assert.False(item.CanRelease);
    }

    [Fact]
    public async Task Claim_rejects_a_user_who_is_not_a_candidate()
    {
        var task = Task("Restricted", Guid.NewGuid().ToString());
        await using var fixture = await Fixture.CreateAsync(task);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ClaimAsync(task.Id, Guid.NewGuid().ToString(), []));
    }

    [Fact]
    public async Task Candidate_identity_matching_is_case_insensitive_across_list_and_claim()
    {
        var userId = Guid.NewGuid().ToString().ToUpperInvariant();
        var task = Task("Case-insensitive", userId.ToLowerInvariant());
        await using var fixture = await Fixture.CreateAsync(task);

        var listed = await fixture.Service.GetTasksForUserAsync(userId, [], UserTaskState.Pending);
        await fixture.Service.ClaimAsync(task.Id, userId, []);

        Assert.Equal(task.Id, Assert.Single(listed).Id);
        await using var db = fixture.CreateContext();
        Assert.Equal(userId, (await db.UserTasks.FindAsync(task.Id))!.AssignedTo);
    }

    [Fact]
    public async Task Legacy_role_candidates_are_honored_by_list_and_claim()
    {
        var userId = Guid.NewGuid().ToString();
        var task = Task("Role task", "CaseWorker");
        await using var fixture = await Fixture.CreateAsync(task);

        var listed = await fixture.Service.QueryForUserAsync(userId, ["caseworker"], new TaskListRequest());
        await fixture.Service.ClaimAsync(task.Id, userId, ["caseworker"]);

        Assert.Equal(task.Id, Assert.Single(listed.Items).Id);
    }

    [Fact]
    public async Task Release_rejects_a_user_other_than_the_assignee()
    {
        var task = Task("Claimed", null, assignedTo: Guid.NewGuid().ToString());
        await using var fixture = await Fixture.CreateAsync(task);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ReleaseAsync(task.Id, Guid.NewGuid().ToString()));
    }

    private static UserTask Task(
        string title,
        string? candidateUser,
        short priority = 0,
        DateTime? due = null,
        string? assignedTo = null) => new()
    {
        Title = title,
        Priority = priority,
        DueDate = due,
        AssignedTo = assignedTo,
        CandidateUsers = candidateUser is null ? null : JsonSerializer.Serialize(new[] { candidateUser }),
        RowVersion = Guid.NewGuid()
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DbContextOptions<ArgentDbContext> options;
        public TaskInboxService Service { get; }

        private Fixture(DbContextOptions<ArgentDbContext> options)
        {
            this.options = options;
            Service = new TaskInboxService(new TestDbContextFactory(options), Mock.Of<IAuditService>());
        }

        public static async Task<Fixture> CreateAsync(params UserTask[] tasks)
        {
            var options = new DbContextOptionsBuilder<ArgentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            await using var db = new ArgentDbContext(options);
            db.UserTasks.AddRange(tasks);
            await db.SaveChangesAsync();
            return new Fixture(options);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ArgentDbContext CreateContext() => new(options);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ArgentDbContext> options)
        : IDbContextFactory<ArgentDbContext>
    {
        public ArgentDbContext CreateDbContext() => new(options);
    }
}
