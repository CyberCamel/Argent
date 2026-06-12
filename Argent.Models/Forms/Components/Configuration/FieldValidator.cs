using System.Text.Json.Serialization;

namespace Argent.Models.Forms.Components.Configuration;

/// <summary>
/// A declarative validation rule attached to a form field. Rules are plain data so the
/// same definition can be evaluated in the designer preview, the live Blazor form, and
/// on the server when a submission is processed.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LengthValidator), "length")]
[JsonDerivedType(typeof(RangeValidator), "range")]
[JsonDerivedType(typeof(RegexValidator), "regex")]
[JsonDerivedType(typeof(EmailValidator), "email")]
[JsonDerivedType(typeof(UrlValidator), "url")]
[JsonDerivedType(typeof(CompareFieldValidator), "compareField")]
[JsonDerivedType(typeof(ExpressionValidator), "expression")]
public abstract class FieldValidator
{
    /// <summary>Error message shown when the rule fails. Falls back to a rule-specific default.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Optional gate: the rule is only evaluated when this condition holds.</summary>
    [JsonPropertyName("when")]
    public Condition? When { get; set; }
}

public class LengthValidator : FieldValidator
{
    [JsonPropertyName("min")]
    public int? Min { get; set; }

    [JsonPropertyName("max")]
    public int? Max { get; set; }
}

public class RangeValidator : FieldValidator
{
    [JsonPropertyName("min")]
    public double? Min { get; set; }

    [JsonPropertyName("max")]
    public double? Max { get; set; }
}

public class RegexValidator : FieldValidator
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";
}

public class EmailValidator : FieldValidator
{
}

public class UrlValidator : FieldValidator
{
}

/// <summary>Compares this field's value against another field's value.</summary>
public class CompareFieldValidator : FieldValidator
{
    [JsonPropertyName("otherField")]
    public string OtherField { get; set; } = "";

    /// <summary>One of the operators in <see cref="CompareOperators.All"/>.</summary>
    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "==";
}

/// <summary>
/// NCalc expression that must evaluate to true. All form values are available as
/// parameters by field name; the field's own value is also bound to <c>value</c>.
/// </summary>
public class ExpressionValidator : FieldValidator
{
    [JsonPropertyName("expression")]
    public string Expression { get; set; } = "";
}
