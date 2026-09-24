using System.Text.Json;
using System.Text.Json.Serialization;

namespace Argent.Core.Serialization;

public class DomainValueJsonConverter : JsonConverter<object?>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.StartObject => JsonDocument.ParseValue(ref reader).RootElement.Clone(),
            JsonTokenType.StartArray => JsonDocument.ParseValue(ref reader).RootElement.Clone(),
            _ => JsonDocument.ParseValue(ref reader).RootElement.Clone()
        };
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object), options);
    }
}
