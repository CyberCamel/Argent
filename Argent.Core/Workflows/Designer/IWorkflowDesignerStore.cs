using Argent.Core.Workflows;

namespace Argent.Core.Workflows.Designer;

public interface IWorkflowDesignerStore
{
    Task<WorkflowDesignerLoadResult?> LoadWorkflowAsync(Guid workflowId);
    Task<WorkflowDesignerLoadResult?> LoadVersionAsync(Guid versionId);
    Task<WorkflowSaveDraftResult> SaveDraftAsync(WorkflowSaveDraftRequest request);
    Task<WorkflowPublishResult> PublishVersionAsync(WorkflowPublishRequest request);
    Task<WorkflowDesignerLoadResult> DeployVersionAsync(Guid versionId, string? userId = null);
    Task SaveRoleAudiencesAsync(Guid versionId, Dictionary<Guid, RoleAudience> audiences);
    Task<Dictionary<Guid, RoleAudience>?> GetVersionAudiencesAsync(Guid versionId);
    Task<WorkflowSaveDraftResult> CreateDraftFromVersionAsync(Guid versionId, string? userId = null);
    Task<WorkflowDesignerLoadResult?> DiscardDraftAsync(Guid draftId, Guid workflowId);
    Task<WorkflowVersionTimeline?> GetVersionTimelineAsync(Guid workflowId);
    Task<WorkflowDiff?> GetDraftDiffAsync(Guid workflowId);
}

public class WorkflowDesignerLoadResult
{
    public Guid WorkflowId { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public WorkflowDefinition? Definition { get; init; }
    public Guid? DraftId { get; init; }
    public Guid? VersionId { get; init; }
    public Dictionary<Guid, RoleAudience> VersionRoleAudiences { get; init; } = [];
}

public class WorkflowSaveDraftRequest
{
    public Guid? WorkflowId { get; init; }
    public Guid? ExistingDraftId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required WorkflowDefinition Definition { get; init; }
    public string? UserId { get; init; }
}

public class WorkflowSaveDraftResult
{
    public Guid WorkflowId { get; init; }
    public Guid DraftId { get; init; }
}

public class WorkflowPublishRequest
{
    public Guid DraftId { get; init; }
    public bool IsMajor { get; init; }
    public Dictionary<Guid, RoleAudience>? InitialAudiences { get; init; }
    public string? UserId { get; init; }
}

public class WorkflowPublishResult
{
    public Guid VersionId { get; init; }
    public required WorkflowDefinition Definition { get; init; }
    public Dictionary<Guid, RoleAudience> RoleAudiences { get; init; } = [];
}

public class WorkflowVersionTimeline
{
    public WorkflowDraftEntry? Draft { get; init; }
    public List<WorkflowVersionEntry> Versions { get; init; } = [];
}

public class WorkflowDraftEntry
{
    public Guid Id { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public class WorkflowVersionEntry
{
    public Guid Id { get; init; }
    public string VersionLabel { get; init; } = "";
    public int VersionMajor { get; init; }
    public int VersionMinor { get; init; }
    public string Name { get; init; } = "";
    public string CreatedBy { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public bool IsDeployed { get; init; }
    public bool IsPublished { get; init; }
    public List<ProcessRole> Roles { get; init; } = [];
}
