namespace Argent.Infrastructure.Data;

/// <summary>
/// Parent entity for a form design. Holds authoring metadata; the actual definition
/// lives in <c>FormDesignDraft</c> (work-in-progress) and <c>FormDesignVersion</c>
/// (published snapshots).
/// </summary>
public class FormDesign
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "Unknown";
}
