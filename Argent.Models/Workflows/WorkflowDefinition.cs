using Argent.Models.Workflows.Modeler;

namespace Argent.Models.Workflows;

[Serializable]
public class WorkflowDefinition
{
    public WorkflowMetadata Metadata { get; set; }
    public List<Connection> Connections { get; set; } = [];
    public List<NodeBase> Nodes { get; set; } = [];
    public Dictionary<Guid, NodeLayout> Layouts { get; set; } = [];
    public List<ProcessRole> Roles { get; set; } = [];
    public List<Pool> Pools { get; set; } = [];
    // Computed at compile time: nodeId → laneId. Empty when no swimlanes are used.
    public Dictionary<Guid, Guid> NodeLanes { get; set; } = [];
}
