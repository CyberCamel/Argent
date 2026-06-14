using Argent.Models.Authorization;
using Argent.Models.Forms.Components.Base;
using System;
using System.Collections.Generic;
using System.Text;
using Argent.Models.Attributes;

namespace Argent.Models.Forms.Components;

[PbacResource("Form")]
public class FormDefinition
{
    [PbacProperty]
    public string FormId { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    [PbacProperty]
    public int Version { get; set; } = 1;
    [PbacProperty]
    public string? Title { get; set; }
    public List<FormComponent> Components { get; set; } = new();
}
