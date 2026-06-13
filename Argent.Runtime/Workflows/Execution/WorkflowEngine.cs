using Argent.Contracts.Workflows.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Argent.Runtime.Workflows.Execution;

public class WorkflowEngine(
    ILogger<WorkflowEngine> logger,
    IServiceProvider serviceProvider,
    RecoveryPass recoveryPass) : BackgroundService
{
    private readonly SemaphoreSlim _semaphore = new(50);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Workflow engine starting...");

        await recoveryPass.RunAsync(stoppingToken);

        logger.LogInformation("Workflow engine started (polling every 1s, max concurrency: 50)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<ClaimedWork> claimed;

                using (var scope = serviceProvider.CreateScope())
                {
                    var claimer = scope.ServiceProvider.GetRequiredService<IWorkClaimer>();
                    claimed = await claimer.ClaimAsync(50, stoppingToken);
                }

                foreach (var work in claimed)
                {
                    await _semaphore.WaitAsync(stoppingToken);

                    var captured = work;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // TokenRunner creates its own scope internally. Pass the stopping
                            // token so in-flight handlers observe shutdown and unwind promptly;
                            // the bounded drain below waits for them to finish.
                            var runner = serviceProvider.GetRequiredService<ITokenRunner>();
                            await runner.RunAsync(captured, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex,
                                "Unhandled exception processing WorkItem {Id}",
                                captured.WorkItemId);
                        }
                        finally
                        {
                            _semaphore.Release();
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Workflow engine tick failed");
            }

            await Task.Delay(1000, stoppingToken);
        }

        logger.LogInformation("Workflow engine stopping...");

        var waitStart = DateTime.UtcNow;
        while (_semaphore.CurrentCount < 50)
        {
            if ((DateTime.UtcNow - waitStart).TotalSeconds > 30)
            {
                logger.LogWarning(
                    "Graceful shutdown timeout. {Count} items still in flight.",
                    50 - _semaphore.CurrentCount);
                break;
            }
            await Task.Delay(500, CancellationToken.None);
        }

        logger.LogInformation("Workflow engine stopped.");
    }
}
