using Argent.Models.Workflows.Modeler;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows;

public class Connection
{
    public string? Expression { get; set; }
    public required NodeBase From { get; set; }
    public required NodeBase To { get; set; }
    public string? Label { get; set; }
}
