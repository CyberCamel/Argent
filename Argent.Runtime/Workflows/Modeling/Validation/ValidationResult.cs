using Argent.Models.Workflows;

namespace Argent.Runtime.Workflows.Modeling.Validation;

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