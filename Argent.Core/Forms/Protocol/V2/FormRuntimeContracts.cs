using System.Text.Json;

namespace Argent.Core.Forms.Protocol.V2;

public sealed class FormBootstrap
{
    public Guid FormDesignId { get; set; }
    public Guid FormVersionId { get; set; }
    public FormDefinition Definition { get; set; } = new();
    public Dictionary<string, JsonElement> InitialValues { get; set; } = [];
    public FormRuntimeMessages Messages { get; set; } = new();
}

public sealed class FormRuntimeMessages
{
    public string Loading { get; set; } = "Loading form…";
    public string InvalidDefinition { get; set; } = "This form cannot be displayed because its definition is invalid.";
    public string SelectPlaceholder { get; set; } = "Select…";
    public string ErrorSummary { get; set; } = "Please correct the highlighted fields.";
    public string Submit { get; set; } = "Submit";
    public string Submitting { get; set; } = "Submitting…";
    public string Submitted { get; set; } = "Your form has been submitted successfully.";
    public string LoadFailed { get; set; } = "Unable to load form.";
    public string SubmissionFailed { get; set; } = "Submission failed.";
    public string SecurityFailed { get; set; } = "Unable to establish a secure submission session.";
    public string NotPublished { get; set; } = "This form is not published.";
}

public sealed class FormSubmitRequest
{
    public string ProtocolVersion { get; set; } = "2.0";
    public Guid SubmissionId { get; set; }
    public Guid FormVersionId { get; set; }
    public Guid? RecordId { get; set; }
    /// <summary>Trusted workflow record bindings, populated by the task endpoint.</summary>
    public Dictionary<string, Guid> RecordIds { get; set; } = [];
    public string? Action { get; set; }
    public Dictionary<string, JsonElement> Values { get; set; } = [];
}

public sealed class FormSubmitResult
{
    public Guid RecordId { get; set; }
    public Dictionary<string, Guid> RecordIds { get; set; } = [];
    public Guid? WorkflowInstanceId { get; set; }
    public bool IsReplay { get; set; }
}

public interface IFormRuntimeService
{
    Task<FormBootstrap?> BootstrapAsync(Guid formDesignId, Guid? recordId = null, CancellationToken cancellationToken = default);
    Task<FormBootstrap?> BootstrapAsync(Guid formDesignId, IReadOnlyDictionary<string, Guid> recordIds, CancellationToken cancellationToken = default);
    Task<FormRuntimeSubmission> SubmitAsync(Guid formDesignId, FormSubmitRequest request, string? user,
        CancellationToken cancellationToken = default, bool updateAttachedRecords = false);
    Task CompleteWorkflowStartAsync(Guid submissionId, Guid workflowInstanceId, CancellationToken cancellationToken = default);
    Task DiscardSubmissionAsync(Guid submissionId, CancellationToken cancellationToken = default);
}

public sealed record FormRuntimeSubmission(FormSubmitResult? Result, IReadOnlyList<FormValueError> Errors)
{
    public bool IsValid => Result is not null && Errors.Count == 0;
}
