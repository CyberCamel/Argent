namespace Argent.Core.Workers;

/// <summary>Serialisation helpers for the JSON columns on <see cref="Worker"/> and <see cref="WorkerRequest"/>.</summary>
public static class WorkerRequestSerialization
{
    public static string WriteSubjects(IEnumerable<string> subjects)
        => System.Text.Json.JsonSerializer.Serialize(subjects as string[] ?? subjects.ToArray());

    public static IReadOnlyList<string> ReadSubjects(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    public static string WriteKeyValues(IReadOnlyDictionary<string, string>? values)
        => System.Text.Json.JsonSerializer.Serialize(
            values as Dictionary<string, string> ?? values?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? []);

    public static IReadOnlyDictionary<string, string> ReadKeyValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
