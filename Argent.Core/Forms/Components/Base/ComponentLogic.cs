using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Forms.Components.Base;

public class ComponentLogic
{
    public string? VisibleIf { get; set; }
    public string? RequiredIf { get; set; }
    public string? Formula { get; set; }
    public bool IsSensitive { get; set; } = false;
}
