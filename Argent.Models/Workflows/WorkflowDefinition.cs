using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows;

[Serializable]
public class WorkflowDefinition
{
    public WorkflowMetadata Metadata { get; set; }
    public List<Connection> Connections { get; set; } = [];
    public List<NodeBase> Nodes { get; set; } = [];

}
