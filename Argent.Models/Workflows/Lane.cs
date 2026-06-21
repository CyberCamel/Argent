using Argent.Models.Workflows.Modeler;

namespace Argent.Models.Workflows;

public class Lane : LayoutElement
{
    public string Label { get; set; } = string.Empty;
    public Guid PoolId { get; set; }
    public Guid? RoleId { get; set; }
    public int Order { get; set; }
}
