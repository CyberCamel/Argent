namespace Argent.Models.Forms;

/// <summary>
/// Stores form field values that have no corresponding <c>DomainProperty</c> on the bound
/// domain object. Keyed to the <c>DomainRecord</c> the form was submitted against and to the
/// <c>FormDesign</c> that defined the extra fields.
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
