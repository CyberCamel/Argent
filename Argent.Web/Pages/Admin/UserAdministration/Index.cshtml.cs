using Argent.Models.Identity;
using Argent.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Admin.UserAdministration;

[Authorize(Policy = "UserAdminOnly")]
public class IndexModel(UserManager<InternalUser> userManager, RoleManager<IdentityRole<Guid>> roleManager) : PageModel
{
    public List<UserViewModel> Users { get; set; } = [];

    public async Task OnGetAsync()
    {
        Users = await BuildUserListAsync();
    }

    private async Task<List<UserViewModel>> BuildUserListAsync()
    {
        var users = userManager.Users.ToList();
        var list = new List<UserViewModel>();
        foreach (var u in users)
        {
            list.Add(new UserViewModel
            {
                Id = u.Id,
                UserName = u.UserName,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Roles = [.. await userManager.GetRolesAsync(u)]
            });
        }
        return list;
    }
}
