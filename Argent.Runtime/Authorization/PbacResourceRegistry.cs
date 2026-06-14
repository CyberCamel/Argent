using System.Reflection;
using Argent.Contracts.Authorization;
using Argent.Models.Attributes;

namespace Argent.Runtime.Authorization;

public sealed class PbacResourceRegistry : IPbacResourceRegistry
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _map;

    public PbacResourceRegistry()
    {
        var modelsAssembly = typeof(PbacResourceAttribute).Assembly;

        _map = modelsAssembly.GetTypes()
            .Select(t => new { Type = t, Attr = t.GetCustomAttribute<PbacResourceAttribute>() })
            .Where(x => x.Attr != null)
            .ToDictionary(
                x => x.Attr!.ResourceName ?? x.Type.Name,
                x => (IReadOnlyList<string>)x.Type
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetCustomAttribute<PbacPropertyAttribute>() != null)
                    .Select(p => "resource." + ToCamelCase(p.Name))
                    .OrderBy(n => n)
                    .ToList()
            );

        ResourceTypeNames = [.. _map.Keys.Order()];
    }

    public IReadOnlyList<string> ResourceTypeNames { get; }

    public IReadOnlyList<string> GetProperties(string resourceTypeName)
        => _map.TryGetValue(resourceTypeName, out var list) ? list : [];

    private static string ToCamelCase(string name)
        => string.IsNullOrEmpty(name) || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name[1..];
}
