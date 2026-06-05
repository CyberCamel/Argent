using Microsoft.AspNetCore.Identity;

namespace Argent.Models.Identity;

public abstract class Person : IdentityUser<Guid>
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }

    // The "Bureaucracy Bag" for random attributes
    public Dictionary<string, string> ExtraAttributes { get; set; } = new();

    // Navigation property: One person can have many positions
    public List<Position> Positions { get; set; } = new();
}
