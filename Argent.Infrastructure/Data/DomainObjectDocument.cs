using Argent.Models.DomainObjects;

namespace Argent.Infrastructure.Data;

public class DomainObjectDocument
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DomainObjectDefinition Definition { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
