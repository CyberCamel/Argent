using Argent.Models.Workflows.Modeler;

namespace Argent.Models.Workflows;

public class Pool : LayoutElement
{
    public string Label { get; set; } = string.Empty;
    public bool IsHorizontal { get; set; } = true;
    public List<Lane> Lanes { get; set; } = [];
}
