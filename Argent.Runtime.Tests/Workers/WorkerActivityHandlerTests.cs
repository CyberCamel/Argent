using Argent.Core.Workers;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Workers;

public class WorkerActivityHandlerTests
{
    private static TokenExecutionContext Context(
        Guid instanceId,
        Guid tokenId,
        Guid nodeId,
        Guid workItemId,
        Dictionary<string, object?>? variables = null) =>
        new(instanceId, tokenId, nodeId, workItemId,
            new TokenVariableBag(variables ?? []), [], null, null);

    private static WorkerActivityHandler MakeHandler(
        IWorkerRequestQueue? queue = null,
        bool registered = true)
    {
        var registry = new Mock<IWorkerRegistry>();
        registry
            .Setup(r => r.IsRegisteredAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(registered);

        return new WorkerActivityHandler(
            queue ?? Mock.Of<IWorkerRequestQueue>(),
            registry.Object);
    }

    [Fact]
    public async Task First_execution_enqueues_the_request_and_waits()
    {
        var instanceId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();

        var queue = new Mock<IWorkerRequestQueue>();
        queue.Setup(q => q.GetForTokenAsync(tokenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkerRequest?)null);
        WorkerRequest? enqueued = null;
        queue.Setup(q => q.EnqueueAsync(It.IsAny<WorkerRequest>(), It.IsAny<CancellationToken>()))
            .Callback<WorkerRequest, CancellationToken>((r, _) => enqueued = r)
            .ReturnsAsync((WorkerRequest r, CancellationToken _) => r);

        var activity = new WorkerActivity
        {
            Id = nodeId,
            Name = "Render report",
            WorkerName = "reports",
            Subject = "render",
            TimeoutSeconds = 120,
            MaxAttempts = 2
        };

        var result = await MakeHandler(queue.Object)
            .ExecuteAsync(activity, Context(instanceId, tokenId, nodeId, workItemId), default);

        Assert.Equal(NodeResultType.Waiting, result.ResultType);
        Assert.NotNull(enqueued);
        Assert.Equal("reports", enqueued!.WorkerName);
        Assert.Equal("render", enqueued.Subject);
        Assert.Equal(instanceId, enqueued.InstanceId);
        Assert.Equal(tokenId, enqueued.TokenId);
        Assert.Equal(nodeId, enqueued.NodeId);
        // The work item id is how completion releases the parked token.
        Assert.Equal(workItemId, enqueued.WorkItemId);
        Assert.Equal(120, enqueued.TimeoutSeconds);
        Assert.Equal(2, enqueued.MaxAttempts);
    }

    [Fact]
    public async Task Parameters_are_interpolated_from_workflow_variables()
    {
        var queue = new Mock<IWorkerRequestQueue>();
        queue.Setup(q => q.GetForTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkerRequest?)null);
        WorkerRequest? enqueued = null;
        queue.Setup(q => q.EnqueueAsync(It.IsAny<WorkerRequest>(), It.IsAny<CancellationToken>()))
            .Callback<WorkerRequest, CancellationToken>((r, _) => enqueued = r)
            .ReturnsAsync((WorkerRequest r, CancellationToken _) => r);

        var activity = new WorkerActivity
        {
            WorkerName = "reports",
            Subject = "render",
            Parameters =
            [
                new WorkerParameter { Key = "customer", Value = "{{CustomerName}}" },
                new WorkerParameter { Key = "static", Value = "literal" }
            ]
        };

        var ctx = Context(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<string, object?> { ["CustomerName"] = "Contoso" });

        await MakeHandler(queue.Object).ExecuteAsync(activity, ctx, default);

        var parameters = enqueued!.GetParameters();
        Assert.Equal("Contoso", parameters["customer"]);
        Assert.Equal("literal", parameters["static"]);
    }

    [Fact]
    public async Task An_unknown_worker_fails_the_node_by_default()
    {
        var activity = new WorkerActivity { WorkerName = "typo", Subject = "render" };

        var result = await MakeHandler(registered: false)
            .ExecuteAsync(activity, Context(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), default);

        // A typo should be visible immediately, not as a workflow that waits forever.
        Assert.False(result.Success);
        Assert.Contains("typo", result.ErrorMessage);
    }

    [Fact]
    public async Task Wait_for_worker_parks_instead_of_failing()
    {
        var queue = new Mock<IWorkerRequestQueue>();
        queue.Setup(q => q.GetForTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkerRequest?)null);
        queue.Setup(q => q.EnqueueAsync(It.IsAny<WorkerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkerRequest r, CancellationToken _) => r);

        var activity = new WorkerActivity { WorkerName = "late", Subject = "render", WaitForWorker = true };

        var result = await MakeHandler(queue.Object, registered: false)
            .ExecuteAsync(activity, Context(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.Equal(NodeResultType.Waiting, result.ResultType);
    }

    [Fact]
    public async Task An_unanswered_request_keeps_waiting()
    {
        var queue = new Mock<IWorkerRequestQueue>();
        queue.Setup(q => q.GetForTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkerRequest { State = WorkerRequestState.Claimed });

        var activity = new WorkerActivity { WorkerName = "reports", Subject = "render" };

        var result = await MakeHandler(queue.Object)
            .ExecuteAsync(activity, Context(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.Equal(NodeResultType.Waiting, result.ResultType);
    }

    [Fact]
    public async Task A_successful_request_becomes_output_variables()
    {
        var request = new WorkerRequest
        {
            State = WorkerRequestState.Succeeded,
            WorkerName = "reports",
            Subject = "render",
            Outputs = WorkerRequestSerialization.WriteKeyValues(new Dictionary<string, string>
            {
                ["reportUrl"] = "https://files/report.pdf",
                ["pageCount"] = "12"
            })
        };

        var queue = new Mock<IWorkerRequestQueue>();
        queue.Setup(q => q.GetForTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var activity = new WorkerActivity { WorkerName = "reports", Subject = "render" };

        var result = await MakeHandler(queue.Object)
            .ExecuteAsync(activity, Context(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.True(result.Success);
        Assert.Equal(NodeResultType.Completed, result.ResultType);
        Assert.Equal("https://files/report.pdf", result.OutputVariables!["reportUrl"]);
        Assert.Equal("12", result.OutputVariables["pageCount"]);
    }

    [Theory]
    [InlineData(WorkerRequestState.Failed, "reported a failure")]
    [InlineData(WorkerRequestState.TimedOut, "timed out")]
    [InlineData(WorkerRequestState.Cancelled, "was cancelled")]
    public async Task A_terminal_failure_fails_the_node_with_the_worker_message(WorkerRequestState state, string expected)
    {
        var request = new WorkerRequest
        {
            State = state,
            WorkerName = "reports",
            Subject = "render",
            ErrorMessage = "the upstream API returned 500"
        };

        var queue = new Mock<IWorkerRequestQueue>();
        queue.Setup(q => q.GetForTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var activity = new WorkerActivity { WorkerName = "reports", Subject = "render" };

        var result = await MakeHandler(queue.Object)
            .ExecuteAsync(activity, Context(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.False(result.Success);
        Assert.Contains(expected, result.ErrorMessage);
        Assert.Contains("the upstream API returned 500", result.ErrorMessage);
    }

    [Fact]
    public async Task A_node_without_a_worker_name_fails()
    {
        var activity = new WorkerActivity { WorkerName = "", Subject = "render" };

        var result = await MakeHandler()
            .ExecuteAsync(activity, Context(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.False(result.Success);
        Assert.Contains("worker name", result.ErrorMessage);
    }

    [Fact]
    public async Task A_node_without_a_subject_fails()
    {
        var activity = new WorkerActivity { WorkerName = "reports", Subject = "" };

        var result = await MakeHandler()
            .ExecuteAsync(activity, Context(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.False(result.Success);
        Assert.Contains("subject", result.ErrorMessage);
    }
}
