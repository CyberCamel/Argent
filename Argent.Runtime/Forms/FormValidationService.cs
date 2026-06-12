using System.Globalization;
using System.Text.RegularExpressions;
using Argent.Contracts.Forms;
using Argent.Models.Forms.Components;
using Argent.Models.Forms.Components.Base;
using Argent.Models.Forms.Components.Configuration;

namespace Argent.Runtime.Forms;

/// <summary>
/// Single source of truth for form validation. Walks the definition tree, skips fields
/// that are not visible, and applies the required flag, the field's inline constraints
/// (min/max length, min/max value) and its <see cref="FieldValidator"/> rules.
/// </summary>
public class FormValidationService(IConditionEvaluator _conditions) : IFormValidator
{
    public Dictionary<string, List<string>> ValidateForm(FormDefinition definition, IFormContext context)
    {
        var errors = new Dictionary<string, List<string>>();
        foreach (var field in EnumerateFields(definition.Components))
        {
            if (string.IsNullOrEmpty(field.Name)) continue;

            var fieldErrors = ValidateField(field, context);
            if (fieldErrors.Count > 0)
                errors[field.Name] = fieldErrors;
        }
        return errors;
    }

    public List<string> ValidateField(FormField field, IFormContext context)
    {
        var errors = new List<string>();

        // Hidden or disabled fields are not validated — they can't be corrected by the user.
        if (!_conditions.EvaluateFieldVisible(field, context)) return errors;
        if (_conditions.EvaluateFieldDisabled(field, context)) return errors;

        var value = context.GetValue(field.Name);
        var text = ValueAsString(value);
        bool isEmpty = string.IsNullOrWhiteSpace(text);

        if (_conditions.EvaluateFieldRequired(field, context) && isEmpty)
        {
            errors.Add($"{Label(field)} is required.");
            return errors; // no point piling constraint errors onto an empty field
        }

        if (isEmpty) return errors; // optional and empty → valid

        ApplyInlineConstraints(field, value, text, errors);

        foreach (var rule in field.Validators)
        {
            if (rule.When != null && !_conditions.Evaluate(rule.When, context))
                continue;

            var failure = Evaluate(rule, field, value, text, context);
            if (failure != null)
                errors.Add(rule.Message ?? failure);
        }

        return errors;
    }

    /// <summary>Constraints configured directly on the field (ExtJS-style shorthand).</summary>
    private static void ApplyInlineConstraints(FormField field, object? value, string text, List<string> errors)
    {
        if (field.MinLength is int minLen && text.Length < minLen)
            errors.Add($"{Label(field)} must be at least {minLen} characters.");

        if (field.MaxLength is int maxLen && text.Length > maxLen)
            errors.Add($"{Label(field)} must be at most {maxLen} characters.");

        if (field.Min.HasValue || field.Max.HasValue)
        {
            if (TryToNumber(value, out var number))
            {
                if (field.Min is double min && number < min)
                    errors.Add($"{Label(field)} must be at least {min}.");
                if (field.Max is double max && number > max)
                    errors.Add($"{Label(field)} must be at most {max}.");
            }
        }
    }

    /// <summary>Returns a default error message when the rule fails, null when it passes.</summary>
    private string? Evaluate(FieldValidator rule, FormField field, object? value, string text, IFormContext context)
    {
        switch (rule)
        {
            case LengthValidator len:
                if (len.Min is int lMin && text.Length < lMin)
                    return $"{Label(field)} must be at least {lMin} characters.";
                if (len.Max is int lMax && text.Length > lMax)
                    return $"{Label(field)} must be at most {lMax} characters.";
                return null;

            case RangeValidator range:
                if (!TryToNumber(value, out var number))
                    return $"{Label(field)} must be a number.";
                if (range.Min is double rMin && number < rMin)
                    return $"{Label(field)} must be at least {rMin}.";
                if (range.Max is double rMax && number > rMax)
                    return $"{Label(field)} must be at most {rMax}.";
                return null;

            case RegexValidator regex:
                if (string.IsNullOrEmpty(regex.Pattern)) return null;
                return SafeIsMatch(text, regex.Pattern)
                    ? null
                    : $"{Label(field)} has an invalid format.";

            case EmailValidator:
                return SafeIsMatch(text, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")
                    ? null
                    : $"{Label(field)} must be a valid email address.";

            case UrlValidator:
                return Uri.TryCreate(text, UriKind.Absolute, out var uri)
                       && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    ? null
                    : $"{Label(field)} must be a valid URL.";

            case CompareFieldValidator cmp:
                if (string.IsNullOrEmpty(cmp.OtherField)) return null;
                var other = context.GetValue(cmp.OtherField);
                var condition = new CompareCondition { Field = field.Name, Operator = cmp.Operator, ValueField = cmp.OtherField };
                return _conditions.Evaluate(condition, context)
                    ? null
                    : $"{Label(field)} does not match {cmp.OtherField}.";

            case ExpressionValidator expr:
                if (string.IsNullOrWhiteSpace(expr.Expression)) return null;
                return EvaluateExpression(expr.Expression, value, context)
                    ? null
                    : $"{Label(field)} is invalid.";

            default:
                return null;
        }
    }

    private static bool EvaluateExpression(string expression, object? value, IFormContext context)
    {
        try
        {
            var e = new NCalc.Expression(expression);
            foreach (var kvp in context.GetAllValues())
                e.Parameters[kvp.Key] = kvp.Value;
            e.Parameters["value"] = value;
            var result = e.Evaluate();
            return result is bool b ? b : Convert.ToBoolean(result);
        }
        catch
        {
            // A broken rule must not block submission — surface nothing rather than a false failure.
            return true;
        }
    }

    private static bool SafeIsMatch(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
        }
        catch
        {
            return true; // invalid pattern or timeout — treat as passing, same rationale as expressions
        }
    }

    private static bool TryToNumber(object? value, out double number)
    {
        switch (value)
        {
            case double d: number = d; return true;
            case int i: number = i; return true;
            case long l: number = l; return true;
            case decimal m: number = (double)m; return true;
            default:
                return double.TryParse(ValueAsString(value), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
        }
    }

    private static string ValueAsString(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "",  // an unchecked checkbox counts as empty for "required"
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static string Label(FormField field) =>
        string.IsNullOrWhiteSpace(field.FieldLabel) ? field.Name : field.FieldLabel;

    private static IEnumerable<FormField> EnumerateFields(List<FormComponent> components)
    {
        foreach (var component in components)
        {
            if (component is FormField field)
                yield return field;
            else if (component is FormLayout layout)
                foreach (var nested in EnumerateFields(layout.Items))
                    yield return nested;
        }
    }
}
