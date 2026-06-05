using Argent.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace Argent.Web;

public static class DbInitializer
{
    public static async Task SeedUsers(UserManager<InternalUser> userManager, RoleManager<IdentityRole<Guid>> roleManager)
    {
        // 1. Ensure all Roles exist (Master Key logic)
        string[] roleNames = ["SuperAdmin", "UserAdmin", "FormAdmin", "FlowAdmin", "User"];

        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }
        }

        // 2. Create the "Multi-Admin"
        // Note: We check by UserName now, not Email!
        var multiUserName = "MultiTool";
        if (await userManager.FindByNameAsync(multiUserName) == null)
        {
            var user = new InternalUser
            {
                UserName = multiUserName,
                Email = "multiadmin@argent.com", // Email is just metadata now
                FirstName = "Multi",
                LastName = "Admin",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, "ArgentPass123!");
            if (result.Succeeded)
            {
                await userManager.AddToRolesAsync(user, new[] { "FormAdmin", "FlowAdmin" });
            }
        }

        // 3. Create the "Super-Admin" (The Boss)
        var superUserName = "Overlord";
        if (await userManager.FindByNameAsync(superUserName) == null)
        {
            var boss = new InternalUser
            {
                UserName = superUserName,
                Email = "boss@argent.com",
                FirstName = "System",
                LastName = "Overlord",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(boss, "UltimateSecret123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(boss, "SuperAdmin");
            }
        }
        var userName = "alexb";
        if (await userManager.FindByNameAsync(superUserName) == null)
        {
            var usr = new InternalUser
            {
                UserName = userName,
                Email = "alex.badiee@gmail.com",
                FirstName = "Alex",
                LastName = "Sandgren",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(usr, "MyPassword123");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(usr, "SuperAdmin");
            }
        }
    }
}
