using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Argent.Core.Forms.Protocol.V2;

public sealed record FormValueError(string Field, string Code, string Message);

public static class FormValueValidator
{
    private static readonly Regex DecimalPattern = new(@"^-?(0|[1-9]\d*)(\.\d+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex DatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant);
    private static readonly Regex TimestampPattern = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant);
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<FormValueError> Validate(FormDefinition definition, IReadOnlyDictionary<string, JsonElement> values)
    {
        var errors = new List<FormValueError>();
        foreach (var field in Fields(definition.Components))
            errors.AddRange(Validate(field, values));
        return errors;
    }

    public static IReadOnlyList<FormValueError> Validate(FormField field, IReadOnlyDictionary<string, JsonElement> values)
    {
        if (field.VisibleWhen is not null && !FormExpressionEvaluator.Evaluate(field.VisibleWhen, values)) return [];
        if (field.DisabledWhen is not null && FormExpressionEvaluator.Evaluate(field.DisabledWhen, values)) return [];

        values.TryGetValue(field.Name, out var value);
        var required = field.Required ||
            (field.RequiredWhen is not null && FormExpressionEvaluator.Evaluate(field.RequiredWhen, values));
        if (required && IsEmpty(value)) return [Error(field, "field.required", $"{field.Label} is required.")];
        if (IsEmpty(value)) return [];

        var errors = new List<FormValueError>();
        if (!HasExpectedType(field, value))
        {
            errors.Add(Error(field, $"type.{field.Type}", $"{field.Label} has an invalid value."));
            return errors;
        }
        // Dynamic references are checked against their target object by FormRuntimeService.
        if (field.Type == "choice" && field.Reference is null &&
            !field.Options.Any(option => option.Value == value.GetString() && !option.Disabled))
            errors.Add(Error(field, "choice.invalid", $"{field.Label} contains an unavailable choice."));

        foreach (var validator in field.Validators)
        {
            if (validator.When is not null && !FormExpressionEvaluator.Evaluate(validator.When, values)) continue;
            if (!Passes(validator, value, values))
                errors.Add(Error(field, validator.Code, validator.Message ?? $"{field.Label} is invalid."));
        }
        return errors;
    }

    private static bool HasExpectedType(FormField field, JsonElement value) => field.Type switch
    {
        "text" or "choice" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "decimal" => value.ValueKind == JsonValueKind.String && DecimalPattern.IsMatch(value.GetString()!),
        "date" => value.ValueKind == JsonValueKind.String && DatePattern.IsMatch(value.GetString()!) &&
                  DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "timestamp" => value.ValueKind == JsonValueKind.String && TimestampPattern.IsMatch(value.GetString()!) &&
                       DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "file" => IsAttachment(value),
        _ => false
    };

    private static bool Passes(FormValidator validator, JsonElement value, IReadOnlyDictionary<string, JsonElement> values) =>
        validator.Type switch
        {
            "required" => !IsEmpty(value),
            "length" => PassesLength(validator, value),
            "range" => PassesRange(validator, value),
            "pattern" => value.ValueKind == JsonValueKind.String &&
                         validator.Pattern is not null && SafeMatch(value.GetString()!, validator.Pattern),
            "email" => value.ValueKind == JsonValueKind.String && EmailPattern.IsMatch(value.GetString()!),
            "url" => value.ValueKind == JsonValueKind.String &&
                     Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https",
            "compare" => PassesComparison(validator, value, values),
            _ => false
        };

    private static bool PassesLength(FormValidator validator, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        var length = value.GetString()!.Length;
        return (!validator.MinLength.HasValue || length >= validator.MinLength) &&
               (!validator.MaxLength.HasValue || length <= validator.MaxLength);
    }

    private static bool PassesRange(FormValidator validator, JsonElement value)
    {
        if (!TryDecimal(value, out var number)) return false;
        if (validator.Min.ValueKind != JsonValueKind.Undefined && (!TryDecimal(validator.Min, out var min) || number < min)) return false;
        if (validator.Max.ValueKind != JsonValueKind.Undefined && (!TryDecimal(validator.Max, out var max) || number > max)) return false;
        return true;
    }

    private static bool PassesComparison(FormValidator validator, JsonElement value, IReadOnlyDictionary<string, JsonElement> values)
    {
        if (!values.TryGetValue(validator.OtherField!, out var other)) return false;
        var expression = new FormExpression
        {
            Operator = validator.Operator ?? "equals",
            Left = new() { Value = value },
            Right = new() { Value = other }
        };
        return FormExpressionEvaluator.Evaluate(expression, values);
    }

    private static bool TryDecimal(JsonElement value, out decimal number) => value.ValueKind switch
    {
        JsonValueKind.Number => value.TryGetDecimal(out number),
        JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number),
        _ => Fail(out number)
    };

    private static bool Fail(out decimal number) { number = default; return false; }

    private static bool SafeMatch(string value, string pattern)
    {
        try { return Regex.IsMatch(value, pattern, RegexOptions.ECMAScript, TimeSpan.FromMilliseconds(100)); }
        catch (ArgumentException) { return false; }
    }

    private static bool IsAttachment(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
        value.TryGetProperty("fileName", out var name) && name.ValueKind == JsonValueKind.String &&
        value.TryGetProperty("contentType", out var contentType) && contentType.ValueKind == JsonValueKind.String &&
        value.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) && bytes >= 0;

    private static bool IsEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => true,
        JsonValueKind.String => string.IsNullOrEmpty(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        _ => false
    };

    private static FormValueError Error(FormField field, string code, string message) => new(field.Name, code, message);

    private static IEnumerable<FormField> Fields(IReadOnlyList<FormComponent> components)
    {
        foreach (var component in components)
            if (component is FormField field) yield return field;
            else if (component is FormLayout layout)
                foreach (var nested in Fields(layout.Children)) yield return nested;
    }
}
