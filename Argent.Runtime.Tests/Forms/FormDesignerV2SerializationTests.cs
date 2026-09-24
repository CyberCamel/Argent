using System.Text.Json;
using Argent.Core.Forms.Protocol.V2;
using Argent.Infrastructure.Serialization;
using Xunit;

namespace Argent.Runtime.Tests.Forms;

public sealed class FormDesignerV2SerializationTests
{
    [Fact]
    public void Field_condition_survives_form_serialization()
    {
        var definition = new FormDefinition
        {
            Id = "conditional-form", ObjectKey = "employee", ViewModes = ["reviewer"],
            Components =
            [
                new FormField { Name = "approved", Label = "Approved", Type = "boolean" },
                new FormField
                {
                    Name = "salary", Label = "Salary", Type = "decimal",
                    VisibleWhen = new FormExpression
                    {
                        Operator = "isNotEmpty", Operand = new FormOperand { Field = "approved" }
                    },
                    RequiredWhen = new FormExpression
                    {
                        Operator = "equals", Left = new FormOperand { Context = "viewMode" },
                        Right = new FormOperand { Value = JsonSerializer.SerializeToElement("reviewer") }
                    }
                }
            ]
        };

        var json = JsonSerializer.Serialize(definition, FormSerializer.Options);
        var restored = JsonSerializer.Deserialize<FormDefinition>(json, FormSerializer.Options)!;
        var field = (FormField)restored.Components[1];

        Assert.Equal("approved", field.VisibleWhen?.Operand?.Field);
        Assert.Equal("viewMode", field.RequiredWhen?.Left?.Context);
        Assert.True(FormDefinitionCompiler.Compile(restored).IsValid);
    }

    [Fact]
    public void Designer_definition_persists_as_runtime_ready_v2_json()
    {
        var definition = new FormDefinition
        {
            Id = "citizen-request",
            ObjectKey = "request",
            Components =
            [
                new FormLayout
                {
                    Id = "contact-section",
                    Type = "section",
                    Children =
                    [
                        new FormField { Type = "text", Name = "email", Label = "Email", Required = true }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(definition, FormSerializer.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("2.0", root.GetProperty("protocolVersion").GetString());
        Assert.Equal("layout", root.GetProperty("components")[0].GetProperty("kind").GetString());
        Assert.Equal("field", root.GetProperty("components")[0].GetProperty("children")[0].GetProperty("kind").GetString());
        Assert.False(json.Contains("editorId", StringComparison.Ordinal));
        Assert.True(FormDefinitionCompiler.Compile(JsonSerializer.Deserialize<FormDefinition>(json, FormSerializer.Options)!).IsValid);
    }
}
