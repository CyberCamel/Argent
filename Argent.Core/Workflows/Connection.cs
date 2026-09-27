using Argent.Core.Forms.Components.Configuration;
using Argent.Core.Workflows.Modeler;

namespace Argent.Core.Workflows;

public class Connection
{
    /// <summary>Stable identity used to attach persisted route intent to this connection.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public string? Expression { get; set; }
    public Condition? Condition { get; set; }
    public bool IsDefault { get; set; }
    public required NodeBase From { get; set; }
    public required NodeBase To { get; set; }
    public string? Label { get; set; }
    public TaskActionPresentation? TaskAction { get; set; }

    /// <summary>
    /// Persisted layout intent. Null for connections that are always routed automatically,
    /// which is the case for every definition written before route intent existed.
    /// </summary>
    public ConnectionRoute? Route { get; set; }
}
