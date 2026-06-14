namespace Argent.Contracts.Authorization;

public interface IPbacResourceRegistry
{
    /// <summary>All resource type names discovered via <c>[PbacResource]</c> attributes, sorted.</summary>
    IReadOnlyList<string> ResourceTypeNames { get; }

    /// <summary>
    /// Returns the camel-cased property paths (e.g. <c>"resource.createdBy"</c>) for the given
    /// resource type. Returns an empty list when the type is unknown or has no <c>[PbacProperty]</c> members.
    /// </summary>
    IReadOnlyList<string> GetProperties(string resourceTypeName);
}
