namespace Argent.Core.Workflows;

public class RoleAudience
{
    public List<string> UserIds { get; set; } = [];
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> GroupIds { get; set; } = [];
}
