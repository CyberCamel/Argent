using System.Text.Json.Serialization;

namespace Argent.Models.Forms.Components.Base;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(FormField), "field")]
[JsonDerivedType(typeof(FormLayout), "container")]
public abstract class FormComponent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("xtype")]
    public string Xtype { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? CssClass { get; set; }
    public string? Style { get; set; }

    [JsonPropertyName("columnIndex")]
    public int ColumnIndex { get; set; }
}