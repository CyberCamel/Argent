using System.Text.Json;
using System.Text.Json.Serialization;

namespace Argent.Core.Serialization;

public static class ArgentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new DomainValueJsonConverter() }
    };
}
