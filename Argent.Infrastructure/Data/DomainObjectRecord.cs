using Argent.Models.DomainObjects;

namespace Argent.Infrastructure.Data;

/// <summary>
/// EF persistence entity for a single domain object instance. <see cref="Values"/> is
/// serialized to a JSON column (same pattern as <c>InternalUser.ExtraAttributes</c>);
/// the runtime <c>DomainRecord</c> DTO is mapped to/from this by the store, resolving
/// the object's system key to <see cref="DomainObjectId"/>.
/// </summary>
public class DomainObjectRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the owning <see cref="DomainObject"/> header.</summary>
    public Guid DomainObjectId { get; set; }
    public DomainObject DomainObject { get; set; } = null!;

    /// <summary>Definition version this record was last written against (schema-on-read).</summary>
    public Version? DefinitionVersion { get; set; }

    /// <summary>Property values keyed by <c>DomainProperty.Key</c>, stored as JSON.</summary>
    public Dictionary<string, object?> Values { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}
