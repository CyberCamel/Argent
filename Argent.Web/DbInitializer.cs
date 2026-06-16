using System.Text;
using Argent.Infrastructure.Data;
using Argent.Models.DomainObjects;
using Argent.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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

        // 4. Seed system groups
        if (!dbContext.Groups.Any(g => g.Id == SystemGroups.AllUsersId))
        {
            dbContext.Groups.Add(new Group
            {
                Id = SystemGroups.AllUsersId,
                Name = SystemGroups.AllUsersName,
                Description = "Automatically includes every user in the system.",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        // 5. Create groups and assign random users
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
        var existingMembershipKeys = dbContext.GroupMemberships
            .Select(m => new { m.GroupId, m.UserId })
            .ToHashSet();

        foreach (var user in allMockUsers)
        {
            var groupCount = rng.Next(1, 3);
            var shuffled = savedGroups.OrderBy(_ => rng.Next()).Take(groupCount);

            foreach (var group in shuffled)
            {
                if (existingMembershipKeys.Contains(new { GroupId = group.Id, UserId = user!.Id })) continue;

                dbContext.GroupMemberships.Add(new GroupMembership
                {
                    GroupId = group.Id,
                    UserId = user.Id
                });
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

    public static async Task SeedDomainObjects(ArgentDbContext dbContext)
    {
        await SeedDomainObject(dbContext,
            key: "invoice",
            name: "Invoice",
            description: "Supplier invoice submitted for approval and payment",
            definition: new DomainObjectDefinition
            {
                Key = "invoice",
                DisplayName = "Invoice",
                PluralName = "Invoices",
                TitleProperty = "invoiceNumber",
                Properties =
                [
                    new() { Key = "invoiceNumber", DisplayName = "Invoice Number", Type = DomainPropertyType.Text,     Required = true,  Unique = true },
                    new() { Key = "vendor",         DisplayName = "Vendor",         Type = DomainPropertyType.Text,     Required = true  },
                    new() { Key = "amount",         DisplayName = "Amount",         Type = DomainPropertyType.Number,   Required = true  },
                    new() { Key = "currency",       DisplayName = "Currency",       Type = DomainPropertyType.Choice,   Required = true,
                        Choices = [new() { Label = "USD", Value = "USD" }, new() { Label = "EUR", Value = "EUR" }, new() { Label = "GBP", Value = "GBP" }, new() { Label = "NOK", Value = "NOK" }] },
                    new() { Key = "issueDate",      DisplayName = "Issue Date",     Type = DomainPropertyType.Date,     Required = true  },
                    new() { Key = "dueDate",        DisplayName = "Due Date",       Type = DomainPropertyType.Date  },
                    new() { Key = "status",         DisplayName = "Status",         Type = DomainPropertyType.Choice,   Required = true,
                        Choices = [new() { Label = "Draft", Value = "draft" }, new() { Label = "Submitted", Value = "submitted" }, new() { Label = "Approved", Value = "approved" }, new() { Label = "Rejected", Value = "rejected" }, new() { Label = "Paid", Value = "paid" }] },
                    new() { Key = "description",    DisplayName = "Description",    Type = DomainPropertyType.MultiLineText },
                    new() { Key = "approvedBy",     DisplayName = "Approved By",    Type = DomainPropertyType.Text  },
                    new() { Key = "notes",          DisplayName = "Notes",          Type = DomainPropertyType.MultiLineText },
                ]
            });

        await SeedDomainObject(dbContext,
            key: "leaveRequest",
            name: "Leave Request",
            description: "Employee leave request submitted for manager approval",
            definition: new DomainObjectDefinition
            {
                Key = "leaveRequest",
                DisplayName = "Leave Request",
                PluralName = "Leave Requests",
                TitleProperty = "employeeId",
                Properties =
                [
                    new() { Key = "employeeId",  DisplayName = "Employee",     Type = DomainPropertyType.Text,   Required = true },
                    new() { Key = "leaveType",   DisplayName = "Leave Type",   Type = DomainPropertyType.Choice, Required = true,
                        Choices = [new() { Label = "Annual", Value = "annual" }, new() { Label = "Sick", Value = "sick" }, new() { Label = "Personal", Value = "personal" }, new() { Label = "Maternity", Value = "maternity" }, new() { Label = "Paternity", Value = "paternity" }] },
                    new() { Key = "startDate",   DisplayName = "Start Date",   Type = DomainPropertyType.Date,   Required = true },
                    new() { Key = "endDate",     DisplayName = "End Date",     Type = DomainPropertyType.Date,   Required = true },
                    new() { Key = "reason",      DisplayName = "Reason",       Type = DomainPropertyType.MultiLineText, Required = true },
                    new() { Key = "status",      DisplayName = "Status",       Type = DomainPropertyType.Choice, Required = true,
                        Choices = [new() { Label = "Pending", Value = "pending" }, new() { Label = "Approved", Value = "approved" }, new() { Label = "Rejected", Value = "rejected" }, new() { Label = "Cancelled", Value = "cancelled" }] },
                    new() { Key = "approvedBy",  DisplayName = "Approved By",  Type = DomainPropertyType.Text },
                    new() { Key = "approvedOn",  DisplayName = "Approved On",  Type = DomainPropertyType.DateTime },
                    new() { Key = "handover",    DisplayName = "Handover Notes", Type = DomainPropertyType.MultiLineText },
                ]
            });

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedDomainObject(
        ArgentDbContext dbContext,
        string key,
        string name,
        string description,
        DomainObjectDefinition definition)
    {
        if (await dbContext.DomainObjects.AnyAsync(o => o.Key == key)) return;

        var obj = new DomainObject
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            Description = description,
            CreatedOn = DateTime.UtcNow,
            UpdatedOn = DateTime.UtcNow
        };
        dbContext.DomainObjects.Add(obj);

        var version = new DomainObjectVersion
        {
            Id = Guid.NewGuid(),
            DomainObjectId = obj.Id,
            Version = new Version(1, 0),
            Name = name,
            Description = description,
            Definition = definition,
            State = DomainObjectState.Published,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        };
        dbContext.DomainObjectVersions.Add(version);
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
