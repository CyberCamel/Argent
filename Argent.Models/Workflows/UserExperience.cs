using System.Text.Json.Serialization;

namespace Argent.Models.Workflows;

[JsonDerivedType(typeof(RedirectExperience), typeDiscriminator: "redirect")]
[JsonDerivedType(typeof(WaitExperience), typeDiscriminator: "wait")]
[JsonDerivedType(typeof(TaskExperience), typeDiscriminator: "task")]
[JsonDerivedType(typeof(FormExperience), typeDiscriminator: "form")]
public abstract record UserExperience();

public record RedirectExperience(string Url) : UserExperience;

public record WaitExperience(string Message, Guid TargetNodeId) : UserExperience;

public record TaskExperience : UserExperience
{
    public required TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public required string FallbackUrl { get; init; }
}

public record FormExperience : UserExperience
{
    public required Guid FormId { get; init; }
    /// <summary>Pinned at workflow publish time. Null until the workflow is first published.</summary>
    public Guid? FormVersionId { get; init; }
    public List<FormVariableMapping> InputMappings { get; init; } = [];
    public List<FormVariableMapping> OutputMappings { get; init; } = [];
}

public record FormVariableMapping
{
    public string FormField { get; init; } = string.Empty;
    public string WorkflowVariable { get; init; } = string.Empty;
}