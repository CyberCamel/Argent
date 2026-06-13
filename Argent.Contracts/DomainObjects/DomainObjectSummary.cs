namespace Argent.Contracts.DomainObjects;

/// <summary>Lightweight listing row for the domain object catalog / pickers.</summary>
public class DomainObjectSummary
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>True when an unpublished draft exists.</summary>
    public bool HasDraft { get; set; }

    /// <summary>The latest published version, or null if never published.</summary>
    public string? PublishedVersion { get; set; }
}
