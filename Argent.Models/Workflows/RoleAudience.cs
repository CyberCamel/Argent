namespace Argent.Models.Workflows;

public class RoleAudience
{
    public List<string> UserIds { get; set; } = [];
    public List<Guid> GroupIds { get; set; } = [];
}
