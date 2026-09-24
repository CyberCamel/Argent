using Argent.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Admin.Groups;

[Authorize(Policy = "SuperAdminOnly")]
public class IndexModel(ArgentDbContext _ctx) : PageModel
{
    public List<GroupRow> Groups { get; set; } = [];

    public async Task OnGetAsync()
    {
        var groups = await _ctx.Groups.OrderBy(g => g.Name).ToListAsync();

        var counts = await _ctx.GroupMemberships
            .GroupBy(m => m.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);

        Groups = groups
            .Select(g => new GroupRow(g.Id, g.Name, g.Description, g.IsSystem, counts.GetValueOrDefault(g.Id)))
            .ToList();
    }

    public record GroupRow(Guid Id, string Name, string? Description, bool IsSystem, int MemberCount);
}
