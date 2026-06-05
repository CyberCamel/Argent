using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Forms.Components.Base;

public class FormLayoutComponent : FormComponent
{
    public List<FormComponent> Children { get; set; } = [];
    public string? Html { get; set; }
}