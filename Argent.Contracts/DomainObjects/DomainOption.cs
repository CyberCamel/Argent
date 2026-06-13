namespace Argent.Contracts.DomainObjects;

/// <summary>A label/value pair projected from records for form dropdowns and pickers.</summary>
public class DomainOption
{
    public object? Value { get; set; }
    public string Label { get; set; } = string.Empty;
}
