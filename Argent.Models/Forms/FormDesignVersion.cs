using Argent.Models.Forms.Components;

namespace Argent.Models.Forms;

/// <summary>A published snapshot of a form design's definition. Mirrors <c>DomainObjectVersion</c>.</summary>
public class FormDesignVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FormDesignId { get; set; }
    public Version Version { get; set; } = new(1, 0);
    public FormDefinition Definition { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
}
