using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Runtime.Workflows.Modeling.Validation;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Modeling;

/// <summary>
/// The server-side Worker Activity design-time rules — the copy that gates publishing. The
/// designer keeps a parallel copy in <c>Argent.WebComponents</c>, covered by
/// <c>Argent.WebComponents.Tests</c>.
/// </summary>
public class WorkerActivityValidationTests
{
    private static WorkflowDefinition Definition(params NodeBase[] nodes) => new()
    {
        Metadata = new WorkflowMetadata
        {
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            Version = new Version(1, 0)
        },
        Nodes = [.. nodes],
        Connections = []
    };

    private static WorkerActivity Worker(
        string workerName = "reports",
        string subject = "render",
        List<WorkerParameter>? parameters = null,
        int maxAttempts = 1) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Render report",
            WorkerName = workerName,
            Subject = subject,
            Parameters = parameters ?? [],
            MaxAttempts = maxAttempts
        };

    private static ValidationResult Validate(WorkflowDefinition definition)
        => new WorkflowValidator().Validate(definition);

    [Fact]
    public void A_worker_node_without_a_worker_or_subject_is_rejected()
    {
        var result = Validate(Definition(Worker(workerName: "", subject: "")));

        // A node the engine could never dispatch is a design error, not a runtime surprise.
        Assert.Contains(result.Errors, e => e.Message.Contains("must name the worker"));
        Assert.Contains(result.Errors, e => e.Message.Contains("must name a subject"));
    }

    [Fact]
    public void A_fully_configured_worker_node_is_accepted()
    {
        var result = Validate(Definition(
            new StartEvent { Id = Guid.NewGuid(), Name = "Start" },
            Worker(),
            new EndEvent { Id = Guid.NewGuid(), Name = "End" }));

        Assert.DoesNotContain(result.Errors, e => e.Message.Contains("worker"));
        Assert.DoesNotContain(result.Warnings, e => e.Message.Contains("worker"));
    }

    [Fact]
    public void A_parameter_without_a_key_is_a_warning_not_an_error()
    {
        var result = Validate(Definition(
            new StartEvent { Id = Guid.NewGuid(), Name = "Start" },
            Worker(parameters: [new WorkerParameter { Key = "", Value = "orphan" }]),
            new EndEvent { Id = Guid.NewGuid(), Name = "End" }));

        Assert.Contains(result.Warnings, w => w.Message.Contains("parameter has no key"));
        Assert.DoesNotContain(result.Errors, e => e.Message.Contains("parameter"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void An_out_of_range_attempt_count_is_a_warning(int maxAttempts)
    {
        var result = Validate(Definition(
            new StartEvent { Id = Guid.NewGuid(), Name = "Start" },
            Worker(maxAttempts: maxAttempts),
            new EndEvent { Id = Guid.NewGuid(), Name = "End" }));

        Assert.Contains(result.Warnings, w => w.Message.Contains("Attempts should be between 1 and 10"));
    }
}
