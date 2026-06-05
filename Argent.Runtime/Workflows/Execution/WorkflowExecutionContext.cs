using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows.Execution;

namespace Argent.Runtime.Workflows.Execution;

public class WorkflowExecutionContext(IWorkflowJournalManager journalManager, WorkflowInstance instance)
{
    public IWorkflowJournalManager JournalManager { get; } = journalManager;
    public WorkflowInstance Instance { get; } = instance;
    public Dictionary<string, object> Variables { get; } = [];

}
