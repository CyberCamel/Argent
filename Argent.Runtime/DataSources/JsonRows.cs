using System.Text.Json;

namespace Argent.Runtime.DataSources;

/// <summary>Projects a JSON response into tabular rows for REST data sources.</summary>
internal static class JsonRows
{
    public static List<Dictionary<string, object?>> Parse(string raw, string? rowsPath)
    {
        var rows = new List<Dictionary<string, object?>>();
        if (string.IsNullOrWhiteSpace(raw)) return rows;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var element = doc.RootElement;

            if (!string.IsNullOrWhiteSpace(rowsPath))
            {
                foreach (var part in rowsPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(part, out var next))
                        element = next;
                    else
                        return rows; // path didn't resolve
                }
            }

            if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray())
                    rows.Add(ToRow(item));
            else if (element.ValueKind == JsonValueKind.Object)
                rows.Add(ToRow(element));
        }
        catch (JsonException)
        {
            // Non-JSON or malformed response: leave rows empty; callers still get Raw.
        }

        return rows;
    }

    private static Dictionary<string, object?> ToRow(JsonElement element)
    {
        var row = new Dictionary<string, object?>();
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var prop in element.EnumerateObject())
                row[prop.Name] = ToValue(prop.Value);
        else
            row["value"] = ToValue(element);
        return row;
    }

    private static object? ToValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => e.GetRawText()
    };
}
