using Argent.Models.Forms.Components.Configuration;

namespace Argent.Models.Workflows;

public class Connection
{
    public string? Expression { get; set; }
    public Condition? Condition { get; set; }
    public bool IsDefault { get; set; }
    public required NodeBase From { get; set; }
    public required NodeBase To { get; set; }
    public string? Label { get; set; }
}
