using Argent.Core.Forms.Components.Configuration;

namespace Argent.Core.Workflows;

public class Connection
{
    public string? Expression { get; set; }
    public Condition? Condition { get; set; }
    public bool IsDefault { get; set; }
    public required NodeBase From { get; set; }
    public required NodeBase To { get; set; }
    public string? Label { get; set; }
    public TaskActionPresentation? TaskAction { get; set; }
}
