using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows.Execution;

namespace Argent.Runtime.Workflows.Execution;

public class WorkflowExecutionContext(WorkflowInstance instance) : IWorkflowExecutionContext
{
    public WorkflowInstance Instance { get; } = instance;
    public Dictionary<string, object> Variables { get; } = [];
    IDictionary<string, object> IWorkflowExecutionContext.Variables => Variables;
}
