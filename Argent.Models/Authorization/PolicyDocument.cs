using Argent.Models.Forms.Components.Configuration;

namespace Argent.Models.Authorization;

public class PolicyDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public PolicyEffect Effect { get; set; } = PolicyEffect.Allow;

    public ResourceType ResourceType { get; set; }
    public string ResourceSelectorJson { get; set; } = "{}";

    public string ActionsJson { get; set; } = "[]";

    public string SubjectJson { get; set; } = "{}";

    public Condition? Condition { get; set; }

    public int Priority { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
