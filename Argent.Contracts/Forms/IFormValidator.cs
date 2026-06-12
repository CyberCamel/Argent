using Argent.Models.Forms.Components;
using Argent.Models.Forms.Components.Base;

namespace Argent.Contracts.Forms;

/// <summary>
/// Evaluates the declarative validation rules of a form definition against the values
/// in a form context. The same implementation backs live (frontend) validation in the
/// Blazor components and server-side validation of submissions.
/// </summary>
public interface IFormValidator
{
    /// <summary>
    /// Validates every visible field in the definition.
    /// Returns a map of field name → error messages; empty when the form is valid.
    /// </summary>
    Dictionary<string, List<string>> ValidateForm(FormDefinition definition, IFormContext context);

    /// <summary>Validates a single field. Returns its error messages (empty when valid).</summary>
    List<string> ValidateField(FormField field, IFormContext context);
}
