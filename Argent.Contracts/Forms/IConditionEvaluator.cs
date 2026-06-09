using Argent.Models.Forms.Components.Base;
using Argent.Models.Forms.Components.Configuration;

namespace Argent.Contracts.Forms;

public interface IConditionEvaluator
{
    bool Evaluate(Condition? condition, IFormContext context);
    bool EvaluateFieldVisible(FormField field, IFormContext context);
    bool EvaluateFieldRequired(FormField field, IFormContext context);
    bool EvaluateFieldDisabled(FormField field, IFormContext context);
    bool EvaluateFieldReadOnly(FormField field, IFormContext context);
}