using Argent.Contracts.Authorization;

namespace Argent.Runtime.Authorization;

public class AuthorizationContext : IAttributeBag
{
    private readonly Dictionary<string, object?> _attributes;

    public AuthorizationContext(
        Dictionary<string, object?> subjectAttributes,
        Dictionary<string, object?> resourceAttributes,
        Dictionary<string, object?>? environment = null)
    {
        _attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in subjectAttributes)
            _attributes[$"subject.{kvp.Key}"] = kvp.Value;

        foreach (var kvp in resourceAttributes)
            _attributes[$"resource.{kvp.Key}"] = kvp.Value;

        if (environment != null)
        {
            foreach (var kvp in environment)
                _attributes[$"env.{kvp.Key}"] = kvp.Value;
        }

        UserRoles = subjectAttributes.TryGetValue("roles", out var roles)
            && roles is List<string> roleList
            ? roleList
            : [];
    }

    public object? GetValue(string key) =>
        _attributes.TryGetValue(key, out var value) ? value : null;

    public Dictionary<string, object?> GetAllValues() => new(_attributes);

    public List<string> UserRoles { get; }
}
