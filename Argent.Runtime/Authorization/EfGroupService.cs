using Argent.Core.Authorization;
using Argent.Infrastructure.Data;
using Argent.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Authorization;

public class EfGroupService(IDbContextFactory<ArgentDbContext> _dbFactory) : IGroupService
{
    public async Task<IReadOnlyList<GroupMemberOption>> GetUsersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Users
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new GroupMemberOption(u.Id.ToString(), u.FirstName + " " + u.LastName + " (" + u.UserName + ")", "bi-person"))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<GroupMemberOption>> GetGroupsAsync(Guid? excludeId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Groups.AsQueryable();
        if (excludeId.HasValue)
            query = query.Where(g => g.Id != excludeId.Value);
        return await query
            .OrderBy(g => g.Name)
            .Select(g => new GroupMemberOption(g.Id.ToString(), g.Name, "bi-people"))
            .ToListAsync();
    }

    public async Task<GroupDetail?> GetAsync(Guid groupId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null) return null;

        var userMemberIds = await db.GroupMemberships
            .Where(m => m.GroupId == groupId)
            .Select(m => m.UserId)
            .ToListAsync();

        var groupMemberIds = await db.GroupGroupMemberships
            .Where(m => m.GroupId == groupId)
            .Select(m => m.MemberGroupId)
            .ToListAsync();

        return new GroupDetail(group.Name, group.Description, group.IsSystem, userMemberIds, groupMemberIds);
    }

    public async Task<Guid> CreateAsync(string name, string? description, IEnumerable<Guid> userIds, IEnumerable<Guid> childGroupIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var group = new Argent.Core.Identity.Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description
        };
        db.Groups.Add(group);

        foreach (var uid in userIds)
            db.GroupMemberships.Add(new Argent.Core.Identity.GroupMembership { GroupId = group.Id, UserId = uid });

        foreach (var gid in childGroupIds)
            db.GroupGroupMemberships.Add(new Argent.Core.Identity.GroupGroupMembership { GroupId = group.Id, MemberGroupId = gid });

        await db.SaveChangesAsync();
        return group.Id;
    }

    public async Task UpdateAsync(Guid groupId, string name, string? description, IEnumerable<Guid> userIds, IEnumerable<Guid> childGroupIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var group = await db.Groups.FindAsync(groupId)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        group.Name = name;
        group.Description = description;

        db.GroupMemberships.RemoveRange(
            await db.GroupMemberships.Where(m => m.GroupId == groupId).ToListAsync());
        db.GroupGroupMemberships.RemoveRange(
            await db.GroupGroupMemberships.Where(m => m.GroupId == groupId).ToListAsync());

        foreach (var uid in userIds)
            db.GroupMemberships.Add(new Argent.Core.Identity.GroupMembership { GroupId = groupId, UserId = uid });

        foreach (var gid in childGroupIds)
            if (gid != groupId)
                db.GroupGroupMemberships.Add(new Argent.Core.Identity.GroupGroupMembership { GroupId = groupId, MemberGroupId = gid });

        await db.SaveChangesAsync();
    }
}
