namespace Argent.Core.Workflows.Activities;

/// <summary>
/// A key/value parameter handed to a worker. <see cref="Value"/> may contain the same
/// <c>{{name}}</c> placeholders that REST activity templates interpolate from workflow variables.
/// </summary>
public class WorkerParameter
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
