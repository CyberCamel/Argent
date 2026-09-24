using System.Text.Json;
using Argent.Core.Forms.Protocol.V2;
using Xunit;

namespace Argent.Runtime.Tests.Forms;

public sealed class FormValueValidationConformanceTests
{
    [Fact]
    public void Dotnet_validator_matches_shared_fixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "validation-conformance.json");
        var suite = JsonSerializer.Deserialize<ValidationSuite>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

        foreach (var testCase in suite.Cases)
        {
            var actual = FormValueValidator.Validate(suite.Definition, testCase.Values)
                .Select(error => new ExpectedError { Field = error.Field, Code = error.Code })
                .OrderBy(error => error.Field).ThenBy(error => error.Code).ToArray();
            var expected = testCase.Errors.OrderBy(error => error.Field).ThenBy(error => error.Code).ToArray();
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        }
    }

    private sealed class ValidationSuite
    {
        public required FormDefinition Definition { get; init; }
        public required List<ValidationCase> Cases { get; init; }
    }

    private sealed class ValidationCase
    {
        public required string Name { get; init; }
        public required Dictionary<string, JsonElement> Values { get; init; }
        public required List<ExpectedError> Errors { get; init; }
    }

    private sealed class ExpectedError
    {
        public required string Field { get; init; }
        public required string Code { get; init; }
    }

    [Fact]
    public void Text_validator_on_non_text_value_returns_an_error_instead_of_throwing()
    {
        var field = new FormField
        {
            Type = "integer",
            Name = "count",
            Label = "Count",
            Validators = [new FormValidator { Type = "email", Code = "count.email" }]
        };
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"count": 3}""")!;

        var error = Assert.Single(FormValueValidator.Validate(field, values));
        Assert.Equal("count.email", error.Code);
    }
}
