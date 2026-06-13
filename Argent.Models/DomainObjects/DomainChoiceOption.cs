namespace Argent.Models.DomainObjects;

/// <summary>
/// A selectable option for a <see cref="DomainPropertyType.Choice"/> property. Kept
/// independent of the Forms layer so the data model has no UI dependency; the form
/// designer maps these onto its own option type when rendering.
/// </summary>
public class DomainChoiceOption
{
    public string Label { get; set; } = string.Empty;
    public object? Value { get; set; }
}
