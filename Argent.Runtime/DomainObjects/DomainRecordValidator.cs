using Argent.Contracts.DomainObjects;
using Argent.Models.DomainObjects;

namespace Argent.Runtime.DomainObjects;

/// <summary>
/// Structural validation of a record against its definition: required fields and basic
/// type compatibility. The richer per-field <c>FieldValidator</c> rules carried on a
/// property are evaluated by the form layer (which already owns that engine); uniqueness
/// needs the database and is enforced in the store.
/// </summary>
public static class DomainRecordValidator
{
    public static IReadOnlyList<DomainValidationError> Validate(
        DomainObjectDefinition definition,
        IDictionary<string, object?> values)
    {
        var errors = new List<DomainValidationError>();

        foreach (var prop in definition.Properties)
        {
            values.TryGetValue(prop.Key, out var value);
            var isEmpty = value is null || (value is string s && string.IsNullOrWhiteSpace(s));

            if (prop.Required && isEmpty)
            {
                errors.Add(new DomainValidationError(prop.Key, $"{Label(prop)} is required."));
                continue;
            }

            if (isEmpty || prop.IsCollection) continue;

            if (!IsTypeCompatible(value!, prop.Type))
                errors.Add(new DomainValidationError(prop.Key, $"{Label(prop)} is not a valid {prop.Type}."));
        }

        return errors;
    }

    private static string Label(DomainProperty prop) =>
        string.IsNullOrWhiteSpace(prop.DisplayName) ? prop.Key : prop.DisplayName;

    private static bool IsTypeCompatible(object value, DomainPropertyType type) => type switch
    {
        DomainPropertyType.Number => value is sbyte or byte or short or int or long or float or double or decimal
                                     || (value is string s && double.TryParse(s, out _)),
        DomainPropertyType.Boolean => value is bool || (value is string bs && bool.TryParse(bs, out _)),
        DomainPropertyType.Date or DomainPropertyType.DateTime
            => value is DateTime || (value is string ds && DateTime.TryParse(ds, out _)),
        DomainPropertyType.Reference => value is Guid || (value is string rs && Guid.TryParse(rs, out _)),
        // Text, MultiLineText, Choice, Json accept any non-empty value.
        _ => true
    };
}
