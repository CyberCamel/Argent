using Argent.Models.Workflows.Activities;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Handlers;

public class JintActivityHandlerTests
{
    private static TokenExecutionContext Context(Dictionary<string, object?> vars) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new TokenVariableBag(vars), [], null, null);

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
        Assert.NotNull(result.ErrorMessage);
    }
}
