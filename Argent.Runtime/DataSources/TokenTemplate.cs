using System.Text.RegularExpressions;

namespace Argent.Runtime.DataSources;

/// <summary>Substitutes <c>{{name}}</c> tokens in REST/SOAP templates with runtime parameter values.</summary>
internal static partial class TokenTemplate
{
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenRegex();

    public static string Apply(string? template, IDictionary<string, object?> parameters)
    {
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;
        return TokenRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return parameters.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : match.Value;
        });
    }
}
