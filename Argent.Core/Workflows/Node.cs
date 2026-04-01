using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows;

public abstract class Node
{
    public List<Connection> OutgoingPaths { get; set; } = [];
    public List<Connection> IncomingPaths { get; set; } = [];

}

public record NodeMetadata()
{
    public required string TypeIdentifier { get; init; }
    public required string DisplayName { get; init; }
    public required string Icon { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
}
