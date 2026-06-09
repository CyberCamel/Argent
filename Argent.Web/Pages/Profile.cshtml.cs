using Argent.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages
{
    [Authorize]
    public class ProfileModel(UserManager<InternalUser> _userManager) : PageModel
    {

        public InternalUser? _user;

        public List<string> UserRoles { get; set; } = []; 

        public List<string> GetUserRoles(InternalUser user)
        {
            var roles = _userManager.GetRolesAsync(user).Result;
            return roles.ToList();
        }

        public async Task OnGetAsync()
        {
            _user = await _userManager.GetUserAsync(User);
            if (_user != null)
            {
                UserRoles = GetUserRoles(_user);
                return;
            }
        }
    }
}
