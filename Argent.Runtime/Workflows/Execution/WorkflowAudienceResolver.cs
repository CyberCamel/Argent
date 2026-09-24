using Argent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Workflows.Execution;

public interface IWorkflowAudienceResolver
{
    /// <summary>
    /// Resolves the audience for a process-local role at the moment a task is created.
    /// Expands group memberships; returns a deduplicated list of user ID strings.
    /// </summary>
    Task<List<string>> ResolveAsync(Guid instanceId, Guid roleId, CancellationToken ct = default);
}

public class WorkflowAudienceResolver(IDbContextFactory<ArgentDbContext> dbFactory) : IWorkflowAudienceResolver
{
    public async Task<List<string>> ResolveAsync(Guid instanceId, Guid roleId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var versionId = await db.WorkflowInstances
            .Where(i => i.InstanceId == instanceId)
            .Select(i => i.VersionId)
            .FirstOrDefaultAsync(ct);

        if (versionId == default) return [];

        var audiences = await db.WorkflowVersions
            .Where(v => v.Id == versionId)
            .Select(v => v.RoleAudiences)
            .FirstOrDefaultAsync(ct);

        if (audiences == null || !audiences.TryGetValue(roleId, out var audience)) return [];

        // Older audience definitions accidentally stored role names in UserIds. Retain support
        // while new definitions use RoleNames explicitly.
        var explicitUserIds = audience.UserIds.Where(value => Guid.TryParse(value, out _)).ToList();
        var roleNames = audience.RoleNames
            .Concat(audience.UserIds.Where(value => !Guid.TryParse(value, out _)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var userIds = new HashSet<string>(explicitUserIds, StringComparer.OrdinalIgnoreCase);

        if (roleNames.Count > 0)
        {
            var roleMembers = await db.UserRoles
                .Join(db.Roles.Where(role => role.Name != null && roleNames.Contains(role.Name)),
                    membership => membership.RoleId, role => role.Id, (membership, _) => membership.UserId)
                .Select(id => id.ToString())
                .ToListAsync(ct);
            foreach (var uid in roleMembers)
                userIds.Add(uid);
        }

        if (audience.GroupIds.Count > 0)
        {
            var groupMembers = await db.GroupMemberships
                .Where(m => audience.GroupIds.Contains(m.GroupId))
                .Select(m => m.UserId.ToString())
                .ToListAsync(ct);

            foreach (var uid in groupMembers)
                userIds.Add(uid);
        }

        return [.. userIds];
    }
}
