using Argent.Contracts.Forms;
using Argent.Models.Forms.Components.Base;
using Argent.Models.Forms.Components.Configuration;

namespace Argent.Runtime.Forms;

public class ConditionEvaluator : IConditionEvaluator
{
    public bool Evaluate(Condition? condition, IFormContext context)
    {
        if (condition == null) return true;

        return condition switch
        {
            AndCondition and => and.All.All(c => Evaluate(c, context)),
            OrCondition or => or.Any.Any(c => Evaluate(c, context)),
            NotCondition not => !Evaluate(not.Not, context),
            CompareCondition comp => EvaluateCompare(comp, context),
            RoleCondition role => EvaluateRole(role, context),
            ExpressionCondition expr => EvaluateExpression(expr, context),
            _ => true
        };
    }

    public bool EvaluateFieldVisible(FormField field, IFormContext context)
    {
        if (field.Hidden) return false;
        return field.VisibleWhen == null || Evaluate(field.VisibleWhen, context);
    }

    public bool EvaluateFieldRequired(FormField field, IFormContext context)
    {
        if (!field.AllowBlank) return true;
        return field.RequiredWhen != null && Evaluate(field.RequiredWhen, context);
    }

    public bool EvaluateFieldDisabled(FormField field, IFormContext context)
    {
        if (field.Disabled) return true;
        return field.DisabledWhen != null && Evaluate(field.DisabledWhen, context);
    }

    public bool EvaluateFieldReadOnly(FormField field, IFormContext context)
    {
        if (field.ReadOnly) return true;
        return field.ReadOnlyWhen != null && Evaluate(field.ReadOnlyWhen, context);
    }

    private bool EvaluateCompare(CompareCondition comp, IFormContext context)
    {
        var fieldValue = context.GetValue(comp.Field);
        var compareValue = comp.ValueField != null
            ? context.GetValue(comp.ValueField)
            : comp.Value;
        return Compare(fieldValue, comp.Operator, compareValue);
    }

    private static bool EvaluateRole(RoleCondition role, IFormContext context)
    {
        var userRoles = context.UserRoles ?? [];
        if (role.Roles.Count > 0 && !role.Roles.Any(r => userRoles.Contains(r)))
            return false;
        if (role.NotRoles.Count > 0 && role.NotRoles.Any(r => userRoles.Contains(r)))
            return false;
        return true;
    }

    private bool EvaluateExpression(ExpressionCondition expr, IFormContext context)
    {
        try
        {
            var e = new NCalc.Expression(expr.Expression);
            foreach (var kvp in context.GetAllValues())
            {
                e.Parameters[kvp.Key] = kvp.Value;
            }
            var result = e.Evaluate();
            return result is bool b ? b : Convert.ToBoolean(result);
        }
        catch
        {
            return true;
        }
    }

    private static bool Compare(object? fieldValue, string op, object? compareValue)
    {
        if (fieldValue == null && compareValue == null)
            return op switch
            {
                "==" => true,
                "!=" => false,
                _ => false
            };
        if (fieldValue == null || compareValue == null)
            return op switch
            {
                "==" => false,
                "!=" => true,
                _ => false
            };

        var comparable = fieldValue as IComparable;

        if (comparable != null && compareValue is IComparable other)
        {
            var cmp = comparable.CompareTo(other);
            return op switch
            {
                "==" => cmp == 0,
                "!=" => cmp != 0,
                ">" => cmp > 0,
                "<" => cmp < 0,
                ">=" => cmp >= 0,
                "<=" => cmp <= 0,
                _ => false
            };
        }

        var strField = fieldValue.ToString() ?? "";
        var strCompare = compareValue.ToString() ?? "";
        var stringCmp = string.Compare(strField, strCompare, StringComparison.OrdinalIgnoreCase);

        return op switch
        {
            "==" => stringCmp == 0,
            "!=" => stringCmp != 0,
            ">" => stringCmp > 0,
            "<" => stringCmp < 0,
            ">=" => stringCmp >= 0,
            "<=" => stringCmp <= 0,
            "contains" => strField.Contains(strCompare, StringComparison.OrdinalIgnoreCase),
            "startsWith" => strField.StartsWith(strCompare, StringComparison.OrdinalIgnoreCase),
            "endsWith" => strField.EndsWith(strCompare, StringComparison.OrdinalIgnoreCase),
            "in" => compareValue is string inStr && inStr.Split(',').Select(s => s.Trim()).Contains(strField),
            "notIn" => compareValue is string notInStr && !notInStr.Split(',').Select(s => s.Trim()).Contains(strField),
            "isEmpty" => string.IsNullOrEmpty(strField),
            "isNotEmpty" => !string.IsNullOrEmpty(strField),
            _ => false
        };
    }
}