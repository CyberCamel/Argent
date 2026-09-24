using Argent.Core.Forms;
using Argent.Core.Forms.Protocol.V2;

namespace Argent.Core.Forms;

public interface IFormDesignerStore
{
    Task<FormDesignerLoadResult?> LoadAsync(Guid formDesignId);
    Task<FormDesignerLoadResult?> LoadVersionAsync(Guid versionId);
    Task<FormDesignerSaveResult> SaveAsync(FormDesignerSaveRequest request);
    Task<FormDesignVersion> PublishAsync(FormPublishRequest request);
    Task<FormDesignerLoadResult?> CreateDraftFromVersionAsync(Guid versionId, string? userName = null);
    Task<IReadOnlyList<FormDesignSummary>> GetSummariesByObjectKeyAsync(string objectKey);
    Task<IReadOnlyList<string>> GetPublishedFieldNamesByObjectKeyAsync(string objectKey);
}

public record FormDesignSummary(Guid Id, string Name);

public class FormDesignerLoadResult
{
    public Guid FormDesignId { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public FormDefinition? Definition { get; init; }
    public Guid? DraftId { get; init; }
    public bool IsReadOnlyVersion { get; init; }
    public List<FormDesignVersion> Versions { get; init; } = [];
}

public class FormDesignerSaveRequest
{
    public Guid? FormDesignId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required FormDefinition Definition { get; init; }
    /// <summary>Display name / username for the UpdatedBy field.</summary>
    public string? UserName { get; init; }
    /// <summary>Identity ID (GUID string) used for ownership grant on new forms.</summary>
    public string? UserIdentityId { get; init; }
}

public class FormDesignerSaveResult
{
    public Guid FormDesignId { get; init; }
    public Guid DraftId { get; init; }
}

public class FormPublishRequest
{
    public Guid FormDesignId { get; init; }
    public string? UserId { get; init; }
}
