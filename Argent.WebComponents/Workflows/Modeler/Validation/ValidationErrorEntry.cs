using Argent.Core.Workflows;

namespace Argent.WebComponents.Workflows.Modeler.Validation;

public class ValidationErrorEntry
{
    public NodeBase? Node { get; set; }
    public string Message { get; set; } = "";
}
