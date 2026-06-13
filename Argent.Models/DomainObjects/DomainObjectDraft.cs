namespace Argent.Models.DomainObjects;

/// <summary>The in-progress working copy of a domain object's definition. Mirrors <c>WorkflowDraft</c>.</summary>
public class DomainObjectDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DomainObjectId { get; set; }
    public DomainObject DomainObject { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DomainObjectDefinition Definition { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
