using Argent.Core.Authorization;
using Argent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Authorization;

public class EfSubjectDirectory(IDbContextFactory<ArgentDbContext> _dbFactory) : ISubjectDirectory
{
    public async Task<IReadOnlyList<SubjectEntry>> GetSubjectsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var users = await db.Users
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new { Id = u.Id.ToString(), u.FirstName, u.LastName, u.UserName })
            .ToListAsync();

        var roles = await db.Roles
            .Where(r => r.Name != null)
            .OrderBy(r => r.Name)
            .Select(r => r.Name!)
            .ToListAsync();

        var groups = await db.Groups
            .OrderBy(g => g.Name)
            .Select(g => new { Id = g.Id.ToString(), g.Name })
            .ToListAsync();

        var result = new List<SubjectEntry>(users.Count + roles.Count + groups.Count);
        result.AddRange(users.Select(u => new SubjectEntry(u.Id, $"{u.FirstName} {u.LastName} ({u.UserName})", "bi-person", IsGroup: false, IsRole: false)));
        result.AddRange(roles.Select(r => new SubjectEntry(r, r, "bi-shield", IsGroup: false, IsRole: true)));
        result.AddRange(groups.Select(g => new SubjectEntry(g.Id, g.Name, "bi-people", IsGroup: true, IsRole: false)));
        return result;
    }
}
