namespace Argent.Models.Identity;

/// <summary>
/// A named collection of users (LDAP-style). Groups are a first-class policy subject —
/// PBAC policies can target a group directly. Membership does not confer roles.
/// </summary>
public class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>System groups are seeded at startup and cannot be modified or deleted.</summary>
    public bool IsSystem { get; set; }
}
