namespace Argent.Models.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class PbacResourceAttribute(string? resourceName = null) : Attribute
{
    /// <summary>
    /// Overrides the default resource type name (the class name) used for policy matching.
    /// Use when the class name differs from the logical resource type name, e.g.
    /// <c>[PbacResource("Form")]</c> on <c>FormDefinition</c>.
    /// </summary>
    public string? ResourceName { get; } = resourceName;
}
