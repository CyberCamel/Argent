using System.Text.Json.Serialization;

namespace Argent.Models.Workflows.Activities;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SetFormFieldAction), "set-form-field")]
[JsonDerivedType(typeof(SetVariableAction), "set-variable")]
public abstract class ScriptAction { }

public class SetFormFieldAction : ScriptAction
{
    public string FieldKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class SetVariableAction : ScriptAction
{
    public string VariableName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
