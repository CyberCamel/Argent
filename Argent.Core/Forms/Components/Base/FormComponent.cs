using Argent.Models.Forms.Components.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Argent.Models.Forms.Components.Base;


[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(FormInputComponent), "input")]
[JsonDerivedType(typeof(FormLayoutComponent), "layout")]
public abstract class FormComponent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty;
    public string? VisibleIf { get; set;  }
    public string? Description { get; set; }
    public string? Width { get; set; }
    public string? CssClass { get; set; }
    public string? Style { get; set; }
    public List<FormComponent> Template { get; set; } = []; // For Repeaters
}
