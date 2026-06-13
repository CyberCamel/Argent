namespace Argent.Models.DomainObjects;

/// <summary>
/// The data type of a <see cref="DomainProperty"/>. Drives storage/serialization,
/// validation, the designer editor, and how forms render a bound field.
/// </summary>
public enum DomainPropertyType
{
    Text,
    MultiLineText,
    Number,
    Boolean,
    Date,
    DateTime,
    /// <summary>A value picked from <see cref="DomainProperty.Choices"/>.</summary>
    Choice,
    /// <summary>A reference to another domain object, stored as the target record's Id.</summary>
    Reference,
    /// <summary>Free-form structured data stored as JSON.</summary>
    Json
}
