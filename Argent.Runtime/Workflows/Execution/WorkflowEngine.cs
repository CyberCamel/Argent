using Argent.Contracts;
using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Runtime.Workflows.Execution;

public class WorkflowEngine(
    ILogger<WorkflowEngine> logger,
    IServiceProvider serviceProvider) // Inject the provider, not the scoped services
    : BackgroundService
{
    private readonly SemaphoreSlim _semaphore = new(50);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Workflow engine started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Executing workflow tick...");

            using (var scope = serviceProvider.CreateScope())
            {
                var workRepository = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
                var router = scope.ServiceProvider.GetRequiredService<IWorkRouter>();

                // 3. Fetch potential work (Lazy cleanup happens inside TryLockWorkItemAsync)
                var availableWork = await workRepository.GetWorkAsync();

                foreach (var workItem in availableWork)
                {
                    if(!await _semaphore.WaitAsync(0, stoppingToken))
                    {
                        logger.LogWarning("Max concurrency reached. Skipping WorkItem {Id} for now.", workItem.Id);
                        continue; // Skip this item for now, it will be retried in the next tick
                    }
                    if (!await workRepository.TryLockWorkItemAsync(workItem.Id)) continue;
                    try
                    {
                        logger.LogInformation("Claimed WorkItem {Id}. Dispatching...", workItem.Id);
                        router.Dispatch(workItem, () => _semaphore.Release());
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to dispatch WorkItem {Id}", workItem.Id);
                    }
                }
            }

            await Task.Delay(5000, stoppingToken);
        }

        logger.LogInformation("Workflow engine stopping.");
    }
}
