using Argent.Core.Forms.Components.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Forms.Components;

public class FormDefinition
{
    public string FormId { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string? Title { get; set; }
    public List<FormComponent> Components { get; set; } = new();
}
