using System.Text.Json;
using Argent.Core.Forms.Protocol.V2;
using Xunit;

namespace Argent.Runtime.Tests.Forms;

public sealed class FormDefinitionCompilerTests
{
    [Fact]
    public void Dotnet_compiler_matches_shared_definition_fixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "definition-conformance.json");
        var suite = JsonSerializer.Deserialize<DefinitionSuite>(File.ReadAllText(path), JsonOptions())!;

        Assert.Equal("2.0", suite.ProtocolVersion);
        foreach (var testCase in suite.Cases)
        {
            var result = FormDefinitionCompiler.Compile(testCase.Definition);
            Assert.Equal(testCase.Valid, result.IsValid);
            Assert.Equal(testCase.ErrorCodes.Order(), result.Errors.Select(error => error.Code).Order());
            if (result.IsValid)
            {
                var actual = result.Definition!.Dependents.ToDictionary(
                    item => item.Key,
                    item => item.Value.Order().ToArray());
                Assert.Equal(JsonSerializer.Serialize(testCase.Dependents), JsonSerializer.Serialize(actual));
            }
        }
    }

    [Fact]
    public void Compile_builds_dependency_index()
    {
        var expression = JsonSerializer.Deserialize<FormExpression>("""
            { "operator":"equals", "left":{"field":"applicantType"}, "right":{"value":"company"} }
            """)!;
        var definition = Definition(
            new FormField { Type = "choice", Name = "applicantType", Label = "Applicant type" },
            new FormField { Type = "text", Name = "companyName", Label = "Company", VisibleWhen = expression });

        var result = FormDefinitionCompiler.Compile(definition);

        Assert.True(result.IsValid);
        Assert.Equal(["components[1]"], result.Definition!.Dependents["applicantType"]);
    }

    [Fact]
    public void Compile_rejects_duplicate_and_unknown_field_references()
    {
        var expression = JsonSerializer.Deserialize<FormExpression>("""
            { "operator":"isNotEmpty", "operand":{"field":"missing"} }
            """)!;
        var definition = Definition(
            new FormField { Type = "text", Name = "name", Label = "Name" },
            new FormField { Type = "text", Name = "name", Label = "Duplicate", RequiredWhen = expression });

        var result = FormDefinitionCompiler.Compile(definition);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "field.name_duplicate");
        Assert.Contains(result.Errors, error => error.Code == "expression.field_unknown");
    }

    [Fact]
    public void Compile_rejects_duplicate_layout_ids_and_unknown_compare_fields()
    {
        var definition = Definition(
            new FormLayout { Type = "section", Id = "same", Children = [] },
            new FormLayout { Type = "row", Id = "same", Children = [] },
            new FormField
            {
                Type = "text", Name = "name", Label = "Name",
                Validators = [new() { Type = "compare", Code = "name.compare", OtherField = "missing", Operator = "equals" }]
            });

        var result = FormDefinitionCompiler.Compile(definition);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "layout.id_duplicate");
        Assert.Contains(result.Errors, error => error.Code == "validator.field_unknown");
    }

    [Fact]
    public void Compile_accepts_guid_style_form_ids_but_keeps_component_identifiers_strict()
    {
        var definition = Definition(
            new FormLayout { Type = "section", Id = "layoutOne", Children = [] });
        definition.Id = "311fc3c9fc8c4b2ba8e5df442cf5828e";

        var valid = FormDefinitionCompiler.Compile(definition);
        definition.Components.Add(new FormField { Type = "text", Name = "123invalid", Label = "Invalid" });
        ((FormLayout)definition.Components[0]).Id = "123invalid";
        var invalid = FormDefinitionCompiler.Compile(definition);

        Assert.True(valid.IsValid);
        Assert.Contains(invalid.Errors, error => error.Code == "field.name_invalid");
        Assert.Contains(invalid.Errors, error => error.Code == "layout.id_invalid");
    }

    [Fact]
    public void Compile_validates_reference_sources()
    {
        var valid = FormDefinitionCompiler.Compile(Definition(new FormField
        {
            Type = "choice", Name = "municipality", Label = "Municipality",
            Reference = new() { ObjectKey = "municipality", LabelField = "name" }
        }));
        var invalid = FormDefinitionCompiler.Compile(Definition(new FormField
        {
            Type = "text", Name = "municipality", Label = "Municipality",
            Reference = new() { ObjectKey = "", LabelField = "name" }
        }));

        Assert.True(valid.IsValid);
        Assert.Contains(invalid.Errors, error => error.Code == "reference.field_type_invalid");
        Assert.Contains(invalid.Errors, error => error.Code == "reference.object_key_invalid");
    }

    private static FormDefinition Definition(params FormComponent[] components) => new()
    {
        ProtocolVersion = "2.0",
        Id = "test-form",
        ObjectKey = "test-object",
        Components = [.. components]
    };

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class DefinitionSuite
    {
        public required string ProtocolVersion { get; init; }
        public required List<DefinitionCase> Cases { get; init; }
    }

    private sealed class DefinitionCase
    {
        public required string Name { get; init; }
        public required FormDefinition Definition { get; init; }
        public required bool Valid { get; init; }
        public required List<string> ErrorCodes { get; init; }
        public required Dictionary<string, string[]> Dependents { get; init; }
    }
}
