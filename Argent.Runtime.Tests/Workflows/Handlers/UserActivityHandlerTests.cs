using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
using Argent.Models.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Handlers;

public class UserActivityHandlerTests
{
    private static TokenExecutionContext Context(Guid instanceId, Guid tokenId, Guid nodeId) =>
        new(instanceId, tokenId, nodeId, new TokenVariableBag([]), [], null, null);

    private static UserActivityHandler MakeHandler(IUserTaskManager manager)
    {
        var resolver = new Mock<IWorkflowAudienceResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return new UserActivityHandler(manager, resolver.Object);
    }

    [Fact]
    public async Task First_execution_creates_task_and_waits()
    {
        var instanceId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        var manager = new Mock<IUserTaskManager>();
        manager.Setup(m => m.GetTaskByTokenAsync(tokenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserTask?)null);

        var activity = new UserActivity
        {
            Id = nodeId,
            Name = "Approve",
            UX = new TaskExperience { Timeout = TimeSpan.FromMinutes(5), FallbackUrl = "https://x" },
        };

        var result = await MakeHandler(manager.Object)
            .ExecuteAsync(activity, Context(instanceId, tokenId, nodeId), default);

        Assert.Equal(NodeResultType.Waiting, result.ResultType);
        manager.Verify(m => m.CreateTaskAsync(
            instanceId, tokenId, nodeId,
            It.Is<DateTime?>(d => d != null),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<short>(),
            It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Completed_task_continues_flow()
    {
        var tokenId = Guid.NewGuid();
        var manager = new Mock<IUserTaskManager>();
        manager.Setup(m => m.GetTaskByTokenAsync(tokenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserTask { TokenId = tokenId, State = UserTaskState.Completed });

        var activity = new UserActivity { Id = Guid.NewGuid(), Name = "Approve" };

        var result = await MakeHandler(manager.Object)
            .ExecuteAsync(activity, Context(Guid.NewGuid(), tokenId, Guid.NewGuid()), default);

        Assert.NotEqual(NodeResultType.Waiting, result.ResultType);
        Assert.True(result.Success);
        manager.Verify(m => m.CreateTaskAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<short>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Pending_task_keeps_waiting()
    {
        var tokenId = Guid.NewGuid();
        var manager = new Mock<IUserTaskManager>();
        manager.Setup(m => m.GetTaskByTokenAsync(tokenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserTask
            {
                TokenId = tokenId,
                State = UserTaskState.Pending,
                DueDate = DateTime.UtcNow.AddHours(1),
            });

        var activity = new UserActivity { Id = Guid.NewGuid(), Name = "Approve" };

        var result = await MakeHandler(manager.Object)
            .ExecuteAsync(activity, Context(Guid.NewGuid(), tokenId, Guid.NewGuid()), default);

        Assert.Equal(NodeResultType.Waiting, result.ResultType);
    }
}
