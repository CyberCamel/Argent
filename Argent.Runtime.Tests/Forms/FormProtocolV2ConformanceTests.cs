using System.Text.Json;
using Argent.Core.Forms.Protocol.V2;
using Xunit;

namespace Argent.Runtime.Tests.Forms;

public sealed class FormProtocolV2ConformanceTests
{
    [Fact]
    public void Dotnet_evaluator_matches_shared_condition_fixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "condition-conformance.json");
        var suite = JsonSerializer.Deserialize<ConformanceSuite>(File.ReadAllText(path), JsonOptions())!;

        Assert.Equal("2.0", suite.ProtocolVersion);
        foreach (var testCase in suite.Cases)
        {
            Assert.Equal(testCase.Expected, FormExpressionEvaluator.Evaluate(testCase.Expression, testCase.Values));
            Assert.Equal(
                testCase.Dependencies.Order(StringComparer.Ordinal),
                FormExpressionEvaluator.Dependencies(testCase.Expression).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Unknown_operator_fails_explicitly()
    {
        var expression = new FormExpression { Operator = "executeDeveloperCode" };
        Assert.Throws<FormProtocolException>(() =>
            FormExpressionEvaluator.Evaluate(expression, new Dictionary<string, JsonElement>()));
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private sealed class ConformanceSuite
    {
        public required string ProtocolVersion { get; init; }
        public required List<ConformanceCase> Cases { get; init; }
    }

    private sealed class ConformanceCase
    {
        public required string Name { get; init; }
        public required Dictionary<string, JsonElement> Values { get; init; }
        public required FormExpression Expression { get; init; }
        public required bool Expected { get; init; }
        public required List<string> Dependencies { get; init; }
    }
}
