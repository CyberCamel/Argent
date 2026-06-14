using System.Text;
using Argent.Infrastructure.Data;
using Argent.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace Argent.Web;

public static class DbInitializer
{
    public static async Task SeedUsers(
        UserManager<InternalUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ArgentDbContext dbContext)
    {
        // 1. Ensure all Roles exist
        string[] roleNames = ["SuperAdmin", "UserAdmin", "FormAdmin", "FlowAdmin", "User"];

        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }
        }

        // 2. Seed admin users
        await SeedAdminUser(userManager, "MultiTool", "multiadmin@argent.com", "Multi", "Admin", "ArgentPass123!",
            ["FormAdmin", "FlowAdmin"]);

        await SeedAdminUser(userManager, "Overlord", "boss@argent.com", "System", "Overlord", "UltimateSecret123!",
            ["SuperAdmin"]);

        await SeedAdminUser(userManager, "alexb", "alex.badiee@gmail.com", "Alex", "Sandgren", "MyPassword123",
            ["SuperAdmin"]);

        // 3. Import mock users from CSV
        var csvPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MOCK_DATA.csv");
        var mockUsers = new List<(string userName, string firstName, string lastName, string email, string password)>();

        if (File.Exists(csvPath))
        {
            var lines = File.ReadAllLines(csvPath);
            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = ParseCsvLine(line);
                if (fields.Count >= 5)
                {
                    mockUsers.Add((fields[0], fields[1], fields[2], fields[3], fields[4]));
                }
            }
        }

        var rng = new Random(42);

        foreach (var (userName, firstName, lastName, email, password) in mockUsers)
        {
            if (await userManager.FindByNameAsync(userName) != null) continue;

            var user = new InternalUser
            {
                UserName = userName,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, "User");
            }
        }

        // 4. Create groups and assign random users
        var groups = new List<Group>
        {
            new() { Name = "Engineering", Description = "Engineering department" },
            new() { Name = "Marketing", Description = "Marketing department" },
            new() { Name = "Sales", Description = "Sales department" },
            new() { Name = "HR", Description = "Human resources" },
            new() { Name = "Finance", Description = "Finance department" }
        };

        foreach (var group in groups)
        {
            if (!dbContext.Groups.Any(g => g.Name == group.Name))
                dbContext.Groups.Add(group);
        }

        await dbContext.SaveChangesAsync();

        // Assign each mock user to 1-2 random groups
        var allMockUsers = mockUsers
            .Select(async mu => await userManager.FindByNameAsync(mu.userName))
            .Select(t => t.GetAwaiter().GetResult())
            .Where(u => u != null)
            .ToList()!;

        var savedGroups = dbContext.Groups.ToList();
        var existingMemberships = dbContext.GroupMemberships.ToHashSet();

        foreach (var user in allMockUsers)
        {
            var groupCount = rng.Next(1, 3);
            var shuffled = savedGroups.OrderBy(_ => rng.Next()).Take(groupCount);

            foreach (var group in shuffled)
            {
                var membership = new GroupMembership
                {
                    GroupId = group.Id,
                    UserId = user!.Id
                };

                if (!existingMemberships.Contains(membership))
                    dbContext.GroupMemberships.Add(membership);
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedAdminUser(
        UserManager<InternalUser> userManager,
        string userName, string email, string firstName, string lastName, string password,
        string[] roles)
    {
        if (await userManager.FindByNameAsync(userName) != null) return;

        var user = new InternalUser
        {
            UserName = userName,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            foreach (var role in roles)
                await userManager.AddToRoleAsync(user, role);
        }
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }
}
