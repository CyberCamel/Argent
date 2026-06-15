using Argent.Models.Identity;
using Argent.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.UserAdministration;

[Authorize(Policy = "UserAdminOnly")]
public class ListModel(UserManager<InternalUser> userManager) : PageModel
{
    public List<UserViewModel> Users { get; set; } = [];

    public async Task<IActionResult> OnGetAsync()
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
        Users = list;
        return Partial("~/Pages/Admin/Users/_UserTablePartial.cshtml", Users);
    }
}
