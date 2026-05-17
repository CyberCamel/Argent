using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows;

[Serializable]
public class WorkflowDefinition
{
    public List<Connection> Connections { get; set; } = [];
    public List<NodeBase> Nodes { get; set; } = [];

}
