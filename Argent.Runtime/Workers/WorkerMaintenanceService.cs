using Argent.Core.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Argent.Runtime.Workers;

/// <summary>
/// Periodic maintenance for the worker subsystem: marks silent workers offline, requeues requests
/// whose lease expired, and fails requests that outlived their author's timeout.
/// Runs in the engine host only, so the web host stays free of background loops.
/// </summary>
public class WorkerMaintenanceService(
    IWorkerRegistry registry,
    IWorkerRequestQueue queue,
    ILogger<WorkerMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    /// <summary>A worker that has not checked in for this long is treated as gone.</summary>
    public static readonly TimeSpan HeartbeatStaleAfter = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Worker maintenance starting (interval {Interval}s, heartbeat stale after {Stale}s)",
            Interval.TotalSeconds, HeartbeatStaleAfter.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await registry.MarkStaleWorkersOfflineAsync(HeartbeatStaleAfter, stoppingToken);
                await queue.RecoverExpiredLeasesAsync(stoppingToken);
                await queue.TimeOutOverdueRequestsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let a sweep failure kill the loop: the next tick retries it.
                logger.LogError(ex, "Worker maintenance pass failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Worker maintenance stopped");
    }
}
