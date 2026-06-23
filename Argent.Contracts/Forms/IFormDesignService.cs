using Argent.Models.Forms;
using Argent.Models.Forms.Components;

namespace Argent.Contracts.Forms;

/// <summary>
/// Design-time authoring of form designs: draft editing and versioned publish.
/// Mirrors <c>IDomainObjectDefinitionService</c>.
/// </summary>
public interface IFormDesignService
{
    Task<FormDesignDraft?> GetDraftAsync(Guid formDesignId);

    Task SaveDraftAsync(Guid formDesignId, FormDefinition definition, string? updatedBy = null);

    /// <summary>Promotes the current draft to a new published version and clears the draft.</summary>
    Task<FormDesignVersion> PublishAsync(Guid formDesignId, string? createdBy = null);

    Task<List<FormDesignVersion>> GetVersionsAsync(Guid formDesignId);

    Task<FormDesignVersion?> GetVersionAsync(Guid versionId);

    /// <summary>The latest published definition for a form, for runtime consumers.</summary>
    Task<FormDefinition?> GetPublishedDefinitionAsync(Guid formDesignId);

    /// <summary>Gets the definition pinned to a specific published version.</summary>
    Task<FormDefinition?> GetVersionDefinitionAsync(Guid versionId);
}
