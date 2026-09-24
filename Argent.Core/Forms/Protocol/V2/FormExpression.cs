using System.Text.Json;
using System.Text.Json.Serialization;

namespace Argent.Core.Forms.Protocol.V2;

/// <summary>
/// Portable protocol-v2 expression tree. It intentionally contains no CLR-specific expression
/// language so the TypeScript and .NET runtimes can implement identical semantics.
/// </summary>
public sealed class FormExpression
{
    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "equals";

    [JsonPropertyName("arguments")]
    public List<FormExpression>? Arguments { get; set; }

    [JsonPropertyName("argument")]
    public FormExpression? Argument { get; set; }

    [JsonPropertyName("left")]
    public FormOperand? Left { get; set; }

    [JsonPropertyName("right")]
    public FormOperand? Right { get; set; }

    [JsonPropertyName("operand")]
    public FormOperand? Operand { get; set; }
}

public sealed class FormOperand
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Value { get; set; }
}

public sealed class FormProtocolException(string message) : Exception(message);
