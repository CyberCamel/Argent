using System.Text.Json;
using System.Text.Json.Serialization;

namespace Argent.Core.Forms.Protocol.V2;

public sealed class FormDefinition
{
    public string ProtocolVersion { get; set; } = "2.0";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ObjectKey { get; set; } = string.Empty;
    public string? Title { get; set; }
    public List<FormComponent> Components { get; set; } = [];
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FormField), "field")]
[JsonDerivedType(typeof(FormLayout), "layout")]
public abstract class FormComponent
{
    [JsonIgnore]
    public string EditorId { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class FormField : FormComponent
{
    public string Type { get; set; } = "text";
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = "Field";
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
    public List<FormOption> Options { get; set; } = [];
    public FormReferenceSource? Reference { get; set; }
    public List<FormValidator> Validators { get; set; } = [];
    public FormExpression? VisibleWhen { get; set; }
    public FormExpression? RequiredWhen { get; set; }
    public FormExpression? DisabledWhen { get; set; }
    public FormExpression? ReadOnlyWhen { get; set; }
}

/// <summary>A server-resolved option source for a single domain-object reference.</summary>
public sealed class FormReferenceSource
{
    public string ObjectKey { get; set; } = string.Empty;
    public string LabelField { get; set; } = string.Empty;
}

public sealed class FormOption
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Disabled { get; set; }
}

public sealed class FormValidator
{
    public string Type { get; set; } = "required";
    public string Code { get; set; } = "field.invalid";
    public string? Message { get; set; }
    public FormExpression? When { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Min { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Max { get; set; }
    public string? Pattern { get; set; }
    public string? OtherField { get; set; }
    public string? Operator { get; set; }
}

public sealed class FormLayout : FormComponent
{
    public string Type { get; set; } = "section";
    public string? Id { get; set; }
    public string? Title { get; set; }
    public FormExpression? VisibleWhen { get; set; }
    public List<FormComponent> Children { get; set; } = [];
}
