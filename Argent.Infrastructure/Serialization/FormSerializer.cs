using System.Text.Json;
using System.Text.Json.Serialization;

namespace Argent.Infrastructure.Serialization;

public static class FormSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
