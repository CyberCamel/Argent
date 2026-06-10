using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Argent.Runtime.Workflows.Execution
{
    public class WorkRouter(
        IServiceScopeFactory _scopeFactory,
        ILogger<WorkRouter> _logger) : IWorkRouter
    {
        public void Dispatch(WorkItem workItem, Action OnComplete)
        {
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
                var db = scope.ServiceProvider.GetRequiredService<ArgentDbContext>();

                try
                {
                    // Look up the workflow definition to find the target node
                    var workflow = await db.WorkflowDefinitions.FindAsync(workItem.DefinitionId);
                    if (workflow?.Definition == null)
                    {
                        _logger.LogWarning("WorkItem {Id}: workflow definition {DefId} not found",
                            workItem.Id, workItem.DefinitionId);
                        await repo.CompleteWorkItemAsync(workItem.Id);
                        OnComplete.Invoke();
                        return;
                    }

                    var node = workflow.Definition.Nodes.FirstOrDefault(n => n.Id == workItem.NodeId);
                    if (node == null)
                    {
                        _logger.LogWarning("WorkItem {Id}: node {NodeId} not found in definition",
                            workItem.Id, workItem.NodeId);
                        await repo.CompleteWorkItemAsync(workItem.Id);
                        OnComplete.Invoke();
                        return;
                    }

                    // Log the dispatch
                    _logger.LogInformation(
                        "Dispatching WorkItem {Id} for node '{NodeName}' ({NodeType}) in workflow '{WorkflowName}'",
                        workItem.Id, node.Name, node.GetType().Name, workflow.Name);

                    // Resolve and execute the handler
                    var handlerType = typeof(IWorkItemHandler);
                    var handler = scope.ServiceProvider.GetService(handlerType) as IWorkItemHandler;

                    if (handler != null)
                    {
                        var ctx = new WorkflowExecutionContext(
                            new NullJournalManager(),
                            new WorkflowInstance
                            {
                                InstanceId = workItem.WorkflowInstanceId,
                                WorkflowId = workItem.DefinitionId
                            });

                        var result = await handler.HandleWorkItemAsync(workItem, ctx);
                        if (!result.Success)
                        {
                            _logger.LogWarning("WorkItem {Id} handler failed: {Error}",
                                workItem.Id, result.ErrorMessage);
                        }
                    }
                    else
                    {
                        // No handler registered — log and complete
                        _logger.LogWarning(
                            "No IWorkItemHandler registered. WorkItem {Id} for node '{NodeName}' ({NodeType}) completed without processing.",
                            workItem.Id, node.Name, node.GetType().Name);
                    }

                    await repo.CompleteWorkItemAsync(workItem.Id);
                    OnComplete.Invoke();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch WorkItem {Id}", workItem.Id);
                    await repo.FreeWorkItemAsync(workItem.Id);
                }
            });
        }
    }

    internal class NullJournalManager : IWorkflowJournalManager
    {
        public void RecordEntry(Models.Workflows.Auditing.WorkflowJournalEntry entry)
        {
            // No-op
        }
    }
}
