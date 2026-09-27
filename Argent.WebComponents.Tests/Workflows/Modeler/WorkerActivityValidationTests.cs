using System;
using System.Collections.Generic;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.WebComponents.Workflows.Modeler.Validation;
using Xunit;

namespace Argent.WebComponents.Tests.Workflows.Modeler;

/// <summary>
/// The designer's copy of the Worker Activity rules, so the Errors panel the author sees is
/// covered here. The server-side copy that gates publishing is covered by
/// <c>Argent.Runtime.Tests.Workflows.Modeling.WorkerActivityValidationTests</c>.
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
        List<WorkerParameter>? parameters = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Render report",
            WorkerName = workerName,
            Subject = subject,
            Parameters = parameters ?? []
        };

    private static ValidationResult Validate(WorkflowDefinition definition)
        => new WorkflowValidator().Validate(definition);

    [Fact]
    public void Clearing_the_worker_and_subject_produces_both_errors()
    {
        var result = Validate(Definition(Worker(workerName: "", subject: "")));

        Assert.Contains(result.Errors, e => e.Message.Contains("must name the worker"));
        Assert.Contains(result.Errors, e => e.Message.Contains("must name a subject"));
    }

    [Fact]
    public void Supplying_the_worker_and_subject_clears_both_errors()
    {
        // The rules track the node rather than latching, so an author who fixes the node sees the
        // errors disappear.
        var result = Validate(Definition(
            new StartEvent { Id = Guid.NewGuid(), Name = "Start" },
            Worker(),
            new EndEvent { Id = Guid.NewGuid(), Name = "End" }));

        Assert.DoesNotContain(result.Errors, e => e.Message.Contains("must name the worker"));
        Assert.DoesNotContain(result.Errors, e => e.Message.Contains("must name a subject"));
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
}
