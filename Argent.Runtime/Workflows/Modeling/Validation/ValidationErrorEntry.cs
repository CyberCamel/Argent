using Argent.Models.Workflows;

namespace Argent.Runtime.Workflows.Modeling.Validation;

public class ValidationErrorEntry
{
    public NodeBase? Node { get; set; }
    public string Message { get; set; }
}