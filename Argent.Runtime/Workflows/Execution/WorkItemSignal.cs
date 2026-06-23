using System.Threading.Channels;

namespace Argent.Runtime.Workflows.Execution;

/// <summary>
/// Singleton wake signal: TokenMovement fires it after writing new work items to the DB;
/// WorkflowEngine waits on it so it can claim work immediately instead of polling on a fixed interval.
/// Bounded at 1 with DropWrite so rapid commits collapse into a single wake-up.
/// </summary>
public sealed class WorkItemSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Signal() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Normal timeout — caller proceeds to claim cycle
        }
    }
}
