namespace Argent.Core.Workflows;

/// <summary>Optional presentation for a user task's outgoing connection; its label remains the routing key.</summary>
public sealed class TaskActionPresentation
{
    public string? Label { get; set; }
    public string? Appearance { get; set; }
    public string? Confirm { get; set; }
}

public sealed record TaskActionDescriptor(string Key, string Label, string Appearance, string? Confirm);

public static class TaskActionDescriptors
{
    public static IReadOnlyList<TaskActionDescriptor> FromConnections(IEnumerable<Connection> connections, Guid nodeId)
    {
        var choices = connections.Where(c => c.From.Id == nodeId && !string.IsNullOrWhiteSpace(c.Label))
            .DistinctBy(c => c.Label, StringComparer.Ordinal).ToList();
        var hasExplicitAppearance = choices.Any(c => c.TaskAction?.Appearance is "primary" or "secondary" or "danger");
        return choices.Select((connection, index) =>
        {
            var presentation = connection.TaskAction;
            var appearance = presentation?.Appearance is "primary" or "secondary" or "danger"
                ? presentation.Appearance
                : !hasExplicitAppearance && index == choices.Count - 1 ? "primary" : "secondary";
            return new TaskActionDescriptor(connection.Label!,
                string.IsNullOrWhiteSpace(presentation?.Label) ? connection.Label! : presentation.Label,
                appearance, string.IsNullOrWhiteSpace(presentation?.Confirm) ? null : presentation.Confirm);
        }).ToList();
    }
}
