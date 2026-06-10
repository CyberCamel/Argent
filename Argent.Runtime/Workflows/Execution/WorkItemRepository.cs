using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Runtime.Workflows.Execution;

public class WorkItemRepository(ArgentDbContext context) : IWorkItemRepository
{
    public async Task<IReadOnlyList<WorkItem>> GetWorkAsync()
    {
        return await context.WorkItems.ToListAsync();
    }
    public async Task<bool> TryLockWorkItemAsync(Guid workItemId)
    {
        var now = DateTime.UtcNow;
        var lockDuration = TimeSpan.FromMinutes(5);
        var updatedRows = await context.WorkItems
            .Where(w => w.Id == workItemId &&
                       (!w.Locked || w.LockExpirationUtc < now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.Locked, true)
                .SetProperty(w => w.LockExpirationUtc, now.Add(lockDuration))
                .SetProperty(w => w.LockedBy, Environment.MachineName)
                .SetProperty(w => w.RetryCount, w => w.RetryCount + 1));

        return updatedRows > 0;
    }
    public async Task FreeWorkItemAsync(Guid workItemId)
    {
        await context.WorkItems
            .Where(w => w.Id == workItemId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.Locked, false)
                .SetProperty(w => w.LockExpirationUtc, (DateTime?)null)
                .SetProperty(w => w.LockedBy, (string?)null));
    }
    public async Task CompleteWorkItemAsync(Guid workItemId)
    {
        var item = await context.WorkItems.FindAsync(workItemId);
        if (item != null)
        {
            context.WorkItems.Remove(item);
            await context.SaveChangesAsync();
        }
    }

    public async Task CreateWorkItemAsync(WorkItem workItem)
    {
        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync();
    }
}