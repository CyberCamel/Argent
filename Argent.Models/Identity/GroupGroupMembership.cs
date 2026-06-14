namespace Argent.Models.Identity;

/// <summary>
/// Nested-group link: the group <see cref="MemberGroupId"/> is a member of (contained by)
/// <see cref="GroupId"/>. Membership is transitive — a user in the child group is treated as a
/// member of the parent.
/// </summary>
public class GroupGroupMembership
{
    public Guid GroupId { get; set; }        // container (parent)
    public Guid MemberGroupId { get; set; }  // child group that is a member of GroupId
}
