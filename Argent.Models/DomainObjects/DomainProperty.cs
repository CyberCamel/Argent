using Argent.Models.Forms.Components.Configuration;

namespace Argent.Models.DomainObjects;

/// <summary>
/// A single field on a <see cref="DomainObjectDefinition"/>. Pure data: the same
/// definition drives the designer, form binding, validation, and the JSON record store.
/// </summary>
public class DomainProperty
{
    /// <summary>Stable, code-facing identifier (e.g. "firstName"). Used as the key in a record's value map.</summary>
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public DomainPropertyType Type { get; set; } = DomainPropertyType.Text;

    public bool Required { get; set; }

    /// <summary>Values must be unique across records of this object.</summary>
    public bool Unique { get; set; }

    /// <summary>Hint that this property is queried/filtered often; candidate for a promoted column later.</summary>
    public bool Indexed { get; set; }

    /// <summary>The property holds a list of values of <see cref="Type"/> (e.g. multi-select, multi-reference).</summary>
    public bool IsCollection { get; set; }

    public object? Default { get; set; }

    /// <summary>Options for <see cref="DomainPropertyType.Choice"/>.</summary>
    public List<DomainChoiceOption> Choices { get; set; } = [];

    /// <summary>For <see cref="DomainPropertyType.Reference"/>: the <see cref="DomainObjectDefinition.Key"/> being referenced.</summary>
    public string? ReferenceTargetKey { get; set; }

    /// <summary>For references: which property on the target to show as the label (defaults to the target's title property).</summary>
    public string? ReferenceDisplayProperty { get; set; }

    /// <summary>Reuses the Forms validator model so designer/form/server share one rule definition.</summary>
    public List<FieldValidator> Validators { get; set; } = [];
}
