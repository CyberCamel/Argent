using Argent.Core.Attributes;

namespace Argent.Core.Workflows;

public record NodeMetadata()
{
    public required Type Type { get; init; }
    public required string DisplayName { get; init; }
    public required string Icon { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public string CssClass { get; set; } = string.Empty;
    public NodeShape Shape { get; set; }
}
