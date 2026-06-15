using Argent.Models.Attributes;
using Argent.Models.Authorization;

namespace Argent.Models.DomainObjects;

/// <summary>
/// A runtime instance of a domain object — the transport/POCO shape exchanged with forms
/// and the workflow engine. <see cref="Values"/> is keyed by <see cref="DomainProperty.Key"/>,
/// matching the dictionary style the engine and Jint activities already work with. The EF
/// persistence entity serializes <see cref="Values"/> to a JSON column separately.
/// </summary>

public class DomainRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning object's <see cref="DomainObjectDefinition.Key"/>.</summary>
    public string ObjectKey { get; set; } = string.Empty;

    /// <summary>The definition version this record was last written against (schema-on-read).</summary>
    public Version? DefinitionVersion { get; set; }

    public Dictionary<string, object?> Values { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
