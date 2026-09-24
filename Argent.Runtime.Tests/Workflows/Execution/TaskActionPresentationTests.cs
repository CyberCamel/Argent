using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Infrastructure.Data;
using Argent.Runtime.Workflows.Stores;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Execution;

public sealed class TaskActionPresentationTests : IntegrationTestBase
{
    [Fact]
    public async Task Stored_task_actions_preserve_routing_keys_and_authored_presentation()
    {
        var start = new StartEvent();
        var task = new UserActivity();
        var end = new EndEvent();
        var seed = await SeedWorkflowAsync(new WorkflowDefinition
        {
            Nodes = [start, task, end],
            Connections =
            [
                new Connection { From = start, To = task },
                new Connection
                {
                    From = task, To = end, Label = "reject",
                    TaskAction = new() { Label = "Close application", Appearance = "danger", Confirm = "Close this application?" }
                },
                new Connection { From = task, To = end, Label = "complete" }
            ]
        });
        var factory = new Mock<IDbContextFactory<ArgentDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreateContext);
        var store = new EfWorkflowTaskStore(factory.Object);

        var actions = await store.GetTaskActionDescriptorsAsync(seed.InstanceId, task.Id);

        Assert.Equal(new TaskActionDescriptor("reject", "Close application", "danger", "Close this application?"), actions[0]);
        Assert.Equal("secondary", actions[1].Appearance);
        Assert.Equal(new[] { "reject", "complete" }, await store.GetTaskActionsAsync(seed.InstanceId, task.Id));
    }

    [Fact]
    public void Legacy_actions_preserve_order_deduplicate_keys_and_default_to_final_primary()
    {
        var task = new UserActivity();
        var end = new EndEvent();
        var actions = TaskActionDescriptors.FromConnections(
        [
            new Connection { From = task, To = end, Label = "return" },
            new Connection { From = task, To = end, Label = "return" },
            new Connection { From = task, To = end, Label = " " },
            new Connection { From = end, To = task, Label = "unrelated" },
            new Connection { From = task, To = end, Label = "complete" }
        ], task.Id);

        Assert.Equal(2, actions.Count);
        Assert.Equal(new TaskActionDescriptor("return", "return", "secondary", null), actions[0]);
        Assert.Equal(new TaskActionDescriptor("complete", "complete", "primary", null), actions[1]);
    }
}
