namespace Argent.Core.Authorization;

public interface IGroupService
{
    Task<IReadOnlyList<GroupMemberOption>> GetUsersAsync();
    Task<IReadOnlyList<GroupMemberOption>> GetGroupsAsync(Guid? excludeId = null);
    Task<GroupDetail?> GetAsync(Guid groupId);
    Task<Guid> CreateAsync(string name, string? description, IEnumerable<Guid> userIds, IEnumerable<Guid> childGroupIds);
    Task UpdateAsync(Guid groupId, string name, string? description, IEnumerable<Guid> userIds, IEnumerable<Guid> childGroupIds);
}

public record GroupMemberOption(string Id, string Label, string Icon);
public record GroupDetail(string Name, string? Description, bool IsSystem, IReadOnlyList<Guid> UserMemberIds, IReadOnlyList<Guid> GroupMemberIds);
