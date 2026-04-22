using Argent.Core.Attributes;
using Argent.Core.Workflows.Modeler.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Modeler;

public class CanvasElement
{
    public Guid Id { get; set; } = new Guid();
    [NodeProperty("Name", "The name of the canvas element", true, PropertyDataType.Text, 0)]
    public string Name { get; set; } = string.Empty;
}
