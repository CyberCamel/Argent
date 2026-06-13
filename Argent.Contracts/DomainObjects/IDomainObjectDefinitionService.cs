using Argent.Models.DomainObjects;

namespace Argent.Contracts.DomainObjects;

/// <summary>
/// Design-time authoring of domain objects: catalog, draft editing, and versioned publish.
/// Mirrors the workflow draft/version lifecycle. Instance data is handled separately by
/// <see cref="IDomainObjectStore"/>.
/// </summary>
public interface IDomainObjectDefinitionService
{
    Task<List<DomainObjectSummary>> GetSummariesAsync();

    Task<DomainObject?> GetAsync(Guid id);

    /// <summary>Creates a new domain object with an initial empty draft.</summary>
    Task<DomainObject> CreateAsync(string key, string name, string? description = null, string? createdBy = null);

    /// <summary>The working definition to edit: the draft if one exists, otherwise the latest published snapshot.</summary>
    Task<DomainObjectDefinition?> GetWorkingDefinitionAsync(Guid id);

    /// <summary>The current published definition resolved by system key, for runtime consumers (forms/workflows).</summary>
    Task<DomainObjectDefinition?> GetPublishedDefinitionAsync(string key);

    /// <summary>Creates or updates the draft definition for the object.</summary>
    Task SaveDraftAsync(Guid id, DomainObjectDefinition definition, string? updatedBy = null);

    /// <summary>Promotes the current draft to a new published version and clears the draft.</summary>
    Task<DomainObjectVersion> PublishAsync(Guid id, string? createdBy = null);

    Task<List<DomainObjectVersion>> GetVersionsAsync(Guid id);
}
