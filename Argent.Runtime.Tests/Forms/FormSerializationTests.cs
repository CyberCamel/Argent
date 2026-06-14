using System.Text.Json;
using Argent.Infrastructure.Data;
using Argent.Infrastructure.Serialization;
using Argent.Models.Forms.Components;
using Argent.Models.Forms.Components.Base;
using Argent.Models.Forms.Components.Configuration;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Argent.Runtime.Tests.Forms;

public class FormSerializationTests
{
    private static FormDefinition CreateFormWithNulls()
    {
        return new FormDefinition
        {
            FormId = "test-form",
            Title = "Test Form",
            Components =
            [
                new FormField
                {
                    Xtype = "TextField",
                    Name = "firstName",
                    FieldLabel = "First Name",
                    Placeholder = null,
                    Value = null,
                    MaxLength = null,
                    MinLength = null
                },
                new FormLayout
                {
                    Xtype = "Row",
                    LayoutType = LayoutType.Flex,
                    Direction = "row",
                    Title = null,
                    Html = null,
                    Align = null,
                    Justify = null,
                    Items =
                    [
                        new FormField
                        {
                            Xtype = "NumericField",
                            Name = "age",
                            FieldLabel = "Age",
                            Min = 0,
                            Max = null,
                            Step = null
                        }
                    ]
                }
            ]
        };
    }

    [Fact]
    public void Serialize_omits_null_properties()
    {
        var form = CreateFormWithNulls();

        var json = JsonSerializer.Serialize(form, FormSerializer.Options);

        Assert.DoesNotContain("\"placeholder\":null", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"value\":null", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"maxLength\":null", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"minLength\":null", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"max\":null", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"step\":null", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"html\":null", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"align\":null", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"justify\":null", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_includes_non_null_properties()
    {
        var form = CreateFormWithNulls();

        var json = JsonSerializer.Serialize(form, FormSerializer.Options);

        Assert.Contains("\"FormId\"", json);
        Assert.Contains("\"Title\"", json);
        Assert.Contains("\"xtype\"", json);
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"fieldLabel\"", json);
        Assert.Contains("\"direction\"", json);
        Assert.Contains("\"min\"", json);
    }

    [Fact]
    public void Round_trip_preserves_equivalence()
    {
        var original = CreateFormWithNulls();

        var json = JsonSerializer.Serialize(original, FormSerializer.Options);
        var deserialized = JsonSerializer.Deserialize<FormDefinition>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.FormId, deserialized.FormId);
        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Components.Count, deserialized.Components.Count);

        var originalField = (FormField)original.Components[0];
        var deserializedField = (FormField)deserialized.Components[0];
        Assert.Equal(originalField.Name, deserializedField.Name);
        Assert.Equal(originalField.FieldLabel, deserializedField.FieldLabel);
        Assert.Equal(originalField.Placeholder, deserializedField.Placeholder);
        Assert.Equal(originalField.Value, deserializedField.Value);
        Assert.Equal(originalField.MaxLength, deserializedField.MaxLength);
        Assert.Equal(originalField.MinLength, deserializedField.MinLength);

        var originalLayout = (FormLayout)original.Components[1];
        var deserializedLayout = (FormLayout)deserialized.Components[1];
        Assert.Equal(originalLayout.LayoutType, deserializedLayout.LayoutType);
        Assert.Equal(originalLayout.Title, deserializedLayout.Title);
        Assert.Equal(originalLayout.Html, deserializedLayout.Html);

        var nestedField = (FormField)originalLayout.Items[0];
        var deserializedNestedField = (FormField)deserializedLayout.Items[0];
        Assert.Equal(nestedField.Min, deserializedNestedField.Min);
        Assert.Equal(nestedField.Max, deserializedNestedField.Max);
        Assert.Equal(nestedField.Step, deserializedNestedField.Step);
    }

    [Fact]
    public void Serialize_with_default_options_includes_nulls()
    {
        var form = CreateFormWithNulls();

        var json = JsonSerializer.Serialize(form);

        Assert.Contains("\"placeholder\":null", json);
        Assert.Contains("\"value\":null", json);
        Assert.Contains("\"maxLength\":null", json);
        Assert.Contains("\"minLength\":null", json);
    }

    [Fact]
    public void Deserialized_null_properties_are_null()
    {
        var form = CreateFormWithNulls();
        var json = JsonSerializer.Serialize(form, FormSerializer.Options);
        var deserialized = JsonSerializer.Deserialize<FormDefinition>(json);

        Assert.NotNull(deserialized);
        var field = (FormField)deserialized.Components[0];
        Assert.Null(field.Placeholder);
        Assert.Null(field.Value);
        Assert.Null(field.MaxLength);
        Assert.Null(field.MinLength);
    }
}
