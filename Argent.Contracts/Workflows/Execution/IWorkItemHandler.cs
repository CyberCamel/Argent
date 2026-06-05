using Argent.Models.Workflows.Execution;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Workflows.Execution;

public interface IWorkItemHandler
{
    Task<ExecutionResult> HandleWorkItemAsync(WorkItem workItem, IWorkflowExecutionContext ctx);
}
