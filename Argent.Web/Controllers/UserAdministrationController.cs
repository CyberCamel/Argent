using Argent.Core.Identity;
using Argent.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Argent.Web.Controllers;

[Authorize(Policy = "UserAdminOnly")]
public class UserAdministrationController(UserManager<InternalUser> _userManager, RoleManager<IdentityRole<Guid>> _roleManager) : Controller
{
    public async Task<IActionResult> Index()
    {
        var users = _userManager.Users.ToList();

        // We have to build the list carefully because of the Async role check
        var viewModel = new List<UserViewModel>();

        foreach (var u in users)
        {
            viewModel.Add(new UserViewModel
            {
                UserName = u.UserName,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Roles = [.. await _userManager.GetRolesAsync(u)] // Use 'await' here!
            });
        }

        return View(viewModel);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return PartialView("_CreateUserPartial", new CreateUserViewModel());
    }
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = 400;
            return PartialView("_CreateUserPartial", model);
        }

        var result = await _userManager.CreateAsync(new InternalUser()
        {
            UserName = model.UserName,
            Email = model.Email,
            FirstName = model.FirstName,
            LastName = model.LastName,
            EmailConfirmed = true
        }, model.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return PartialView("_CreateUserPartial", model);
        }

        // HTMX trigger instead of redirect
        Response.Headers["HX-Trigger"] = "userActionCompleted";

        return Ok();
    }
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var users = _userManager.Users.ToList();

        var viewModel = new List<UserViewModel>();

        foreach (var u in users)
        {
            viewModel.Add(new UserViewModel
            {
                UserName = u.UserName,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Roles = [.. await _userManager.GetRolesAsync(u)]
            });
        }

        return PartialView("_UserTablePartial", viewModel);
    }
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByNameAsync(id);
        if (user == null) return NotFound();

        var model = new EditUserViewModel
        {
            UserName = user.UserName,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Roles = [.. (await _userManager.GetRolesAsync(user))],
            AvailableRoles = [.. _roleManager.Roles.Select(r => r.Name ?? "Unknown role")]
        };

        return PartialView("_EditUserPartial", model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return PartialView("_EditUserPartial", model); // 200 for validation
        }

        var user = await _userManager.FindByNameAsync(model.UserName);
        if (user == null) return NotFound();

        user.FirstName = model.FirstName;
        user.LastName = model.LastName;
        user.Email = model.Email;

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return PartialView("_EditUserPartial", model);
        }

        Response.Headers["HX-Trigger"] = "userActionCompleted";
        return Ok();
    }
    [HttpDelete]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userManager.FindByNameAsync(id);
        if (user == null) return NotFound();

        var result = await _userManager.DeleteAsync(user);

        if (!result.Succeeded)
        {
            // optional: return error partial
            return BadRequest(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        Response.Headers["HX-Trigger"] = "userDeleted";
        return Ok();
    }
}
