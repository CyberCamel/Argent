namespace Argent.Core.Forms;

/// <summary>
/// Stores form-only field values that have no object binding. Keyed to the form's
/// primary domain record and the form design that defined the fields.
/// </summary>
public class FormCustomData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecordId { get; set; }
    public Guid FormId { get; set; }
    public Dictionary<string, object?> Values { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
