using System.Text.Json;
using Argent.Models.DomainObjects;

namespace Argent.Runtime.DomainObjects;

/// <summary>
/// Coerces loosely-typed values into the CLR shape implied by a property's type. Records
/// round-trip through JSON storage as <see cref="JsonElement"/>, so reads need normalizing
/// before forms/workflows consume them; writes accept whatever the caller supplies.
/// </summary>
internal static class DomainValueCoercion
{
    public static Dictionary<string, object?> Coerce(IDictionary<string, object?> values, DomainObjectDefinition? definition)
    {
        var byKey = definition?.Properties.ToDictionary(p => p.Key, StringComparer.Ordinal);
        var result = new Dictionary<string, object?>(values.Count, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            DomainProperty? prop = null;
            byKey?.TryGetValue(key, out prop);
            result[key] = CoerceValue(value, prop);
        }

        return result;
    }

    private static object? CoerceValue(object? value, DomainProperty? prop)
    {
        if (value is null) return null;

        // Only JsonElement needs unwrapping; values supplied directly by callers pass through.
        if (value is not JsonElement element)
            return value;

        if (prop is { IsCollection: true } && element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Select(e => CoerceScalar(e, prop.Type)).ToList();

        return CoerceScalar(element, prop?.Type ?? DomainPropertyType.Text);
    }

    private static object? CoerceScalar(JsonElement element, DomainPropertyType type)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.String => CoerceString(element, type),
            // Objects/arrays for Json-typed (or unexpected) properties: keep the raw JSON.
            _ => element.GetRawText()
        };
    }

    private static object? CoerceString(JsonElement element, DomainPropertyType type)
    {
        var s = element.GetString();
        if (s is null) return null;

        return type switch
        {
            DomainPropertyType.Date or DomainPropertyType.DateTime
                => DateTime.TryParse(s, out var dt) ? dt : s,
            DomainPropertyType.Reference
                => Guid.TryParse(s, out var g) ? g : s,
            DomainPropertyType.Number
                => double.TryParse(s, out var d) ? d : s,
            _ => s
        };
    }
}
