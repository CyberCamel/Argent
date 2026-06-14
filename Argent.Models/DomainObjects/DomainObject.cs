using Argent.Models.Attributes;
using Argent.Models.Authorization;
using Argent.Models.Identity;

namespace Argent.Models.DomainObjects;

/// <summary>
/// The header/aggregate row for a domain object, analogous to <c>Workflow</c>. The
/// authored shape lives in versions and a working draft; instances (records) link back
/// to this via <see cref="Id"/>.
/// </summary>

[PbacResource]
public class DomainObject
{
    [PbacProperty]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable system name, unique across the platform (e.g. "customer").</summary>
    [PbacProperty]
    public string Key { get; set; } = string.Empty;

    [PbacProperty]
    public string Name { get; set; } = "New Domain Object";
    [PbacProperty]
    public string Description { get; set; } = string.Empty;

    [PbacProperty]
    public Guid? CreatedById { get; set; }
    public InternalUser? CreatedBy { get; set; }
    public Guid? UpdatedById { get; set; }
    public InternalUser? UpdatedBy { get; set; }
    [PbacProperty]
    public DateTime CreatedOn { get; set; }
    public DateTime UpdatedOn { get; set; }

    [PbacProperty]
    public List<string> Tags { get; set; } = [];
}
