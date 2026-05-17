using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows;

public abstract record UserExperience();
public record RedirectExperience(string Url): UserExperience;
public record WaitExperience(string Message, Guid TargetNodeId) : UserExperience;
public record TaskExperience : UserExperience
{
    public required TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public required string FallbackUrl { get; init; }
}