using Argent.Core.Forms.Components.Configuration;

namespace Argent.Core.Authorization;

public interface IConditionEvaluator
{
    bool Evaluate(Condition condition, IAttributeBag context);
}
