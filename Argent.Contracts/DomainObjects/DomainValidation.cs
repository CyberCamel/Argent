namespace Argent.Contracts.DomainObjects;

/// <summary>A single validation failure against a domain record, keyed by property.</summary>
public class DomainValidationError
{
    public string Property { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public DomainValidationError() { }

    public DomainValidationError(string property, string message)
    {
        Property = property;
        Message = message;
    }
}

/// <summary>
/// Thrown by the store when a record fails structural validation against its definition.
/// Carries per-property errors so the form layer can surface them on the right fields.
/// </summary>
public class DomainValidationException : Exception
{
    public IReadOnlyList<DomainValidationError> Errors { get; }

    public DomainValidationException(IReadOnlyList<DomainValidationError> errors)
        : base($"Domain record validation failed with {errors.Count} error(s).")
    {
        Errors = errors;
    }
}
