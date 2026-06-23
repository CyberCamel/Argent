using Argent.Models.Forms.Components;

namespace Argent.Models.Forms;

/// <summary>The in-progress working copy of a form design's definition. Mirrors <c>DomainObjectDraft</c>.</summary>
public class FormDesignDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FormDesignId { get; set; }
    public FormDefinition Definition { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;
}
