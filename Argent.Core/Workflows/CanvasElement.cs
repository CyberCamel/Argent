using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows;

public class CanvasElement
{
    public Guid Id { get; set; } = new Guid();
    public string Name { get; set; } = string.Empty;
}
