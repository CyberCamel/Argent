using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows.Execution;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Runtime.Workflows.Execution
{
    public class WorkRouter(IServiceScopeFactory _scopeFactory) : IWorkRouter
    {
        public void Dispatch(WorkItem workItem, Action OnComplete)
        {
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();

                try
                {
                    // Logic to find the right handler and execute
                    // handler.Process(workItem);

                    await repo.CompleteWorkItemAsync(workItem.Id);
                    OnComplete.Invoke();
                }
                catch (Exception)
                {
                    // Lazy cleanup will catch this if we don't explicitly unlock
                    await repo.FreeWorkItemAsync(workItem.Id);
                }
            });
        }
    }
}
