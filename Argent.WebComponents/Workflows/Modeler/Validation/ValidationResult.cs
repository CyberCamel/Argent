using Argent.Core.Workflows;

namespace Argent.WebComponents.Workflows.Modeler.Validation;

public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationErrorEntry> Errors = [];
    public List<ValidationErrorEntry> Warnings = [];

    public void AddWarning(NodeBase node, string warning)
    {
        Warnings.Add(new ValidationErrorEntry { Node = node, Message = warning });
    }

    public void AddError(NodeBase node, string error)
    {
        Errors.Add(new ValidationErrorEntry { Node = node, Message = error });
    }
}
