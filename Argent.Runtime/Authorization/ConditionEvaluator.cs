using System.Collections;
using System.Globalization;
using Argent.Core.Authorization;
using Argent.Core.Forms.Components.Configuration;

namespace Argent.Runtime.Authorization;

public sealed class ConditionEvaluator : IConditionEvaluator
{
    public bool Evaluate(Condition condition, IAttributeBag context) => condition switch
    {
        AndCondition and => and.All.Count > 0 && and.All.All(item => Evaluate(item, context)),
        OrCondition or => or.Any.Count > 0 && or.Any.Any(item => Evaluate(item, context)),
        NotCondition not => !Evaluate(not.Not, context),
        RoleCondition role => role.Roles.All(context.UserRoles.Contains) &&
                              role.NotRoles.All(item => !context.UserRoles.Contains(item)),
        CompareCondition compare => Compare(compare, context),
        ExpressionCondition expression => EvaluateExpression(expression.Expression, context),
        _ => false
    };

    private static bool Compare(CompareCondition condition, IAttributeBag context)
    {
        var left = context.GetValue(condition.Field);
        var right = string.IsNullOrWhiteSpace(condition.ValueField)
            ? condition.Value
            : context.GetValue(condition.ValueField);
        return condition.Operator switch
        {
            "==" => EqualsNormalized(left, right),
            "!=" => !EqualsNormalized(left, right),
            ">" => Order(left, right) > 0,
            "<" => Order(left, right) < 0,
            ">=" => Order(left, right) >= 0,
            "<=" => Order(left, right) <= 0,
            "contains" => left?.ToString()?.Contains(right?.ToString() ?? string.Empty, StringComparison.Ordinal) == true,
            "startsWith" => left?.ToString()?.StartsWith(right?.ToString() ?? string.Empty, StringComparison.Ordinal) == true,
            "endsWith" => left?.ToString()?.EndsWith(right?.ToString() ?? string.Empty, StringComparison.Ordinal) == true,
            "in" => Contains(right, left),
            "notIn" => !Contains(right, left),
            "isEmpty" => IsEmpty(left),
            "isNotEmpty" => !IsEmpty(left),
            _ => false
        };
    }

    private static bool EqualsNormalized(object? left, object? right) =>
        left is null || right is null ? left is null && right is null :
        decimal.TryParse(left.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l) &&
        decimal.TryParse(right.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var r)
            ? l == r
            : string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);

    private static int Order(object? left, object? right)
    {
        if (left is null || right is null) return int.MinValue;
        if (decimal.TryParse(left.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l) &&
            decimal.TryParse(right.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
            return l.CompareTo(r);
        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    private static bool Contains(object? collection, object? value) => collection is IEnumerable items &&
        items.Cast<object?>().Any(item => EqualsNormalized(item, value));

    private static bool IsEmpty(object? value) => value is null || value is string text && string.IsNullOrEmpty(text) ||
        value is ICollection collection && collection.Count == 0;

    // Arbitrary expression conditions remain a policy/workflow concern. Portable form rules use
    // Protocol v2 expression trees and never enter this evaluator.
    private static bool EvaluateExpression(string expression, IAttributeBag context)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;
        var evaluator = new NCalc.Expression(expression);
        evaluator.EvaluateParameter += (name, args) => args.Result = context.GetValue(name);
        return evaluator.Evaluate() is true;
    }
}
