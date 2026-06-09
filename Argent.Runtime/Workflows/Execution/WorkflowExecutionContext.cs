using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows.Execution;

namespace Argent.Runtime.Workflows.Execution;

public class WorkflowExecutionContext(IWorkflowJournalManager journalManager, WorkflowInstance instance) : IWorkflowExecutionContext
{
    public IWorkflowJournalManager JournalManager { get; } = journalManager;
    public WorkflowInstance Instance { get; } = instance;
    public Dictionary<string, object> Variables { get; } = [];
    IDictionary<string, object> IWorkflowExecutionContext.Variables => Variables;

}
