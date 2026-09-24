namespace Argent.Core.Forms.Protocol.V2;

/// <summary>
/// Placeholder detection for form field labels. Control-palette factories seed new fields
/// with their type display name ("Text", "Integer", …) as the label. When such a field is
/// bound to a domain property the placeholder must be replaced with the property's display
/// name; this helper centralises "is this label still a generic placeholder?" so both the
/// designer binding logic and data repairs agree on the definition.
/// </summary>
public static class FormFieldLabels
{
    private static readonly HashSet<string> PlaceholderLabels = new(StringComparer.Ordinal)
    {
        "Field",
        "Text",
        "Integer",
        "Decimal",
        "Date",
        "Timestamp",
        "Checkbox",
        "Choice",
        "Reference"
    };

    public static bool IsPlaceholder(string? label) =>
        string.IsNullOrWhiteSpace(label) || PlaceholderLabels.Contains(label);
}