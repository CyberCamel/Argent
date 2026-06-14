using Argent.Models.Forms.Components;

namespace Argent.Infrastructure.Data;

/// <summary>
/// Storage entity for a <see cref="FormDefinition"/> blueprint. Holds authoring metadata
/// alongside the serialised definition. One <c>FormDesign</c> maps to exactly one
/// <c>DomainObject</c> via <see cref="ObjectKey"/>.
/// </summary>
public class FormDesign
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public FormDefinition? Definition { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "Unknown";
}
