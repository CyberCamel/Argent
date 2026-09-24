namespace Argent.Infrastructure.Data;

/// <summary>
/// Durable result of a client submission key. This makes ordinary retries safe while the
/// wider cross-store transaction and reconciliation strategy remains a hardening concern.
/// </summary>
public sealed class FormSubmissionReceipt
{
    public Guid Id { get; set; }
    public Guid FormDesignId { get; set; }
    public Guid FormVersionId { get; set; }
    public Guid RecordId { get; set; }
    public string RecordBindingsJson { get; set; } = "{}";
    public Guid? WorkflowInstanceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
