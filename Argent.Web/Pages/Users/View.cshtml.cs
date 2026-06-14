using Argent.Infrastructure.Data;
using Argent.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Users;

[Authorize(Policy = "UserAdminOnly")]
public class ViewModel(
    UserManager<InternalUser> userManager,
    IDbContextFactory<ArgentDbContext> dbFactory) : PageModel
{
    public UserDetailModel Detail { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound();

        await using var db = await dbFactory.CreateDbContextAsync();

        var groups = await db.GroupMemberships
            .Where(m => m.UserId == id)
            .Join(db.Groups, m => m.GroupId, g => g.Id, (_, g) => g.Name)
            .OrderBy(n => n)
            .ToListAsync();

        Detail = new UserDetailModel
        {
            Id = user.Id,
            UserName = user.UserName,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Department = user.Department,
            IsManager = user.IsManager,
            Roles = [.. await userManager.GetRolesAsync(user)],
            Groups = groups
        };

        return Page();
    }

    public record UserDetailModel
    {
        public Guid Id { get; set; }
        public string? UserName { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Department { get; set; }
        public bool IsManager { get; set; }
        public List<string> Roles { get; set; } = [];
        public List<string> Groups { get; set; } = [];
    }
}
