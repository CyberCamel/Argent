using Argent.Core.DomainObjects;
using Argent.Core.DomainObjects.Querying;
using Argent.Core.Forms;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Handlers;

public class JintActivityHandlerTests
{
    private static TokenExecutionContext Context(
        Dictionary<string, object?> vars,
        Guid recordId = default,
        Guid? formId = null,
        string objectKey = "") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new TokenVariableBag(vars), [], null, null,
            recordId, formId, objectKey);

    [Fact]
    public async Task Evaluates_script_against_variables_and_returns_result()
    {
        var activity = new JintActivity
        {
            Id = Guid.NewGuid(),
            Name = "Compute",
            Code = "x * 2",
            ReturnVariable = "doubled",
        };

        var result = await new JintActivityHandler()
            .ExecuteAsync(activity, Context(new() { ["x"] = 21 }), default);

        Assert.True(result.Success);
        Assert.Equal(42d, Convert.ToDouble(result.OutputVariables!["doubled"]));
    }

    [Fact]
    public async Task Injects_parameters_into_scope()
    {
        var activity = new JintActivity
        {
            Id = Guid.NewGuid(),
            Name = "Compute",
            Code = "factor + 1",
            Parameters = new Dictionary<string, object> { ["factor"] = 9 },
            ReturnVariable = "out",
        };

        var result = await new JintActivityHandler()
            .ExecuteAsync(activity, Context([]), default);

        Assert.True(result.Success);
        Assert.Equal(10d, Convert.ToDouble(result.OutputVariables!["out"]));
    }

    [Fact]
    public async Task No_return_variable_yields_no_output()
    {
        var activity = new JintActivity { Id = Guid.NewGuid(), Name = "Side", Code = "1 + 1" };

        var result = await new JintActivityHandler()
            .ExecuteAsync(activity, Context([]), default);

        Assert.True(result.Success);
        Assert.Null(result.OutputVariables);
    }

    [Fact]
    public async Task Jint_can_set_and_read_instance_values()
    {
        var activity = new JintActivity
        {
            Id = Guid.NewGuid(),
            Name = "SetValue",
            Code = "argent.SetInstanceValue('fromScript', 12); return argent.GetInstanceValue('fromScript');",
            ReturnVariable = "value"
        };

        var result = await new JintActivityHandler()
            .ExecuteAsync(activity, Context([]), default);

        Assert.True(result.Success);
        Assert.Equal(12d, Convert.ToDouble(result.OutputVariables!["fromScript"]));
        Assert.Equal(12d, Convert.ToDouble(result.OutputVariables!["value"]));
    }

    [Fact]
    public async Task Jint_can_update_a_current_domain_form_value()
    {
        var recordId = Guid.NewGuid();
        var formId = Guid.NewGuid();
        var record = new DomainRecord
        {
            Id = recordId,
            ObjectKey = "invoice",
            Values = new Dictionary<string, object?> { ["vendor"] = "Old Vendor" }
        };
        var updated = new DomainRecord
        {
            Id = recordId,
            ObjectKey = "invoice",
            Values = new Dictionary<string, object?> { ["vendor"] = "New Vendor" }
        };
        var domainStore = new Mock<IDomainObjectStore>();
        var formStore = new Mock<IFormDataStore>();
        var definitions = new Mock<IDomainObjectDefinitionService>();
        domainStore.SetupSequence(store => store.GetAsync("invoice", recordId))
            .ReturnsAsync(record)
            .ReturnsAsync(updated);
        domainStore.Setup(store => store.UpdateAsync("invoice", recordId, It.IsAny<IDictionary<string, object?>>(), null))
            .ReturnsAsync(updated);
        formStore.Setup(store => store.GetCustomDataAsync(recordId, formId))
            .ReturnsAsync([]);
        definitions.Setup(store => store.GetPublishedDefinitionAsync("invoice"))
            .ReturnsAsync(new DomainObjectDefinition
            {
                Key = "invoice",
                Properties = [new DomainProperty { Key = "vendor" }]
            });

        var activity = new JintActivity
        {
            Id = Guid.NewGuid(),
            Name = "UpdateValue",
            Code = "argent.SetFormValue('vendor', 'New Vendor'); return argent.GetFormValue('vendor');",
            ReturnVariable = "value"
        };

        var result = await new JintActivityHandler(
                domainStore.Object,
                formStore.Object,
                definitions.Object)
            .ExecuteAsync(activity, Context([], recordId, formId, "invoice"), default);

        Assert.True(result.Success);
        Assert.Equal("New Vendor", result.OutputVariables!["value"]);
        domainStore.Verify(store => store.UpdateAsync(
            "invoice",
            recordId,
            It.Is<IDictionary<string, object?>>(values => (string?)values["vendor"] == "New Vendor"),
            null), Times.Once);
    }

    [Fact]
    public void Jint_domain_queries_are_bounded_to_the_current_object()
    {
        var domainStore = new Mock<IDomainObjectStore>();
        domainStore.Setup(store => store.QueryAsync("invoice", It.IsAny<DomainQuery>()))
            .ReturnsAsync(new DomainQueryResult
            {
                TotalCount = 1,
                Records =
                [
                    new DomainRecord
                    {
                        Id = Guid.NewGuid(),
                        ObjectKey = "invoice",
                        Values = new Dictionary<string, object?> { ["vendor"] = "Argent Labs" }
                    }
                ]
            });
        var api = new JintWorkflowScriptApi(
            Context([], Guid.NewGuid(), null, "invoice"),
            domainStore.Object,
            null,
            null);

        var records = api.QueryDomainRecords(1000);

        Assert.Single(records);
        Assert.Equal("Argent Labs", records[0]["values"] is Dictionary<string, object?> values
            ? values["vendor"]
            : null);
        domainStore.Verify(store => store.QueryAsync("invoice", It.Is<DomainQuery>(query => query.Take == 25)));
    }

    [Fact]
    public async Task Jint_can_query_the_current_domain_object()
    {
        var recordId = Guid.NewGuid();
        var domainStore = new Mock<IDomainObjectStore>();
        domainStore.Setup(store => store.QueryAsync("invoice", It.IsAny<DomainQuery>()))
            .ReturnsAsync(new DomainQueryResult
            {
                TotalCount = 1,
                Records =
                [
                    new DomainRecord
                    {
                        Id = recordId,
                        ObjectKey = "invoice",
                        Values = new Dictionary<string, object?> { ["vendor"] = "Argent Labs" }
                    }
                ]
            });
        var activity = new JintActivity
        {
            Id = Guid.NewGuid(),
            Name = "Query",
            Code = "return argent.QueryDomainRecords(1000).length;",
            ReturnVariable = "count"
        };

        var result = await new JintActivityHandler(
                domainStore.Object,
                Mock.Of<IFormDataStore>(),
                Mock.Of<IDomainObjectDefinitionService>())
            .ExecuteAsync(activity, Context([], recordId, null, "invoice"), default);

        Assert.True(result.Success);
        Assert.Equal(1d, Convert.ToDouble(result.OutputVariables!["count"]));
        domainStore.Verify(store => store.QueryAsync("invoice", It.Is<DomainQuery>(query => query.Take == 25)));
    }

    [Fact]
    public async Task Script_error_returns_failed_result()
    {
        var activity = new JintActivity
        {
            Id = Guid.NewGuid(),
            Name = "Bad",
            Code = "throw new Error('boom')",
            ReturnVariable = "x",
        };

        var result = await new JintActivityHandler()
            .ExecuteAsync(activity, Context([]), default);

        Assert.False(result.Success);
        Assert.Equal(NodeResultType.Failed, result.ResultType);
        Assert.NotNull(result.ErrorMessage);
    }
}
