namespace Argent.Models.Identity;

/// <summary>Join row linking an <see cref="InternalUser"/> to a <see cref="Group"/>.</summary>
public class GroupMembership
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
}
