using Argent.Models.Workflows.Execution;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Workflows.Execution;

public interface IWorkItemRepository
{
    Task<IReadOnlyList<WorkItem>> GetWorkAsync();
    Task<bool> TryLockWorkItemAsync(Guid workItemId);
    Task CompleteWorkItemAsync(Guid workItemId);
    Task FreeWorkItemAsync(Guid workItemId);
}
