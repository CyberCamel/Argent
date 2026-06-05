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
    // Don't store WorkItems in a class-level field. 
    // In a distributed system, this state is "poison" because it won't 
    // survive a crash or a restart.


    private readonly SemaphoreSlim _semaphore = new(50);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Workflow engine started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Executing workflow tick...");

            // 1. Create the scope
            using (var scope = serviceProvider.CreateScope())
            {
                // 2. Resolve your scoped services from the scope
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
                    // 4. Try to claim the lock (Atomic operation)
                    if (!await workRepository.TryLockWorkItemAsync(workItem.Id)) continue;
                    try
                    {
                        logger.LogInformation("Claimed WorkItem {Id}. Dispatching...", workItem.Id);
                        router.Dispatch(workItem, () => _semaphore.Release());
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to dispatch WorkItem {Id}", workItem.Id);
                        // Optional: explicitly unlock here, or let Lazy Cleanup handle it
                    }
                }
            } // The scope is disposed here (DbContext is closed/returned to pool)

            await Task.Delay(5000, stoppingToken);
        }

        logger.LogInformation("Workflow engine stopping.");
        // Note: You can't easily "Free" all items here because 
        // the instance might be killed instantly. 
        // This is why your "Lazy Cleanup" is so important!
    }
}
