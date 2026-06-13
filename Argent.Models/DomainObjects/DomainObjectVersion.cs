namespace Argent.Models.DomainObjects;

/// <summary>A published (or draft-promoted) snapshot of a domain object's definition. Mirrors <c>WorkflowVersion</c>.</summary>
public class DomainObjectVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DomainObjectId { get; set; }
    public DomainObject DomainObject { get; set; } = null!;
    public Version Version { get; set; } = new(1, 0);
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DomainObjectDefinition Definition { get; set; } = new();
    public DomainObjectState State { get; set; } = DomainObjectState.Draft;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
