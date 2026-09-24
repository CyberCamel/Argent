using Argent.Core.Workflows.Execution;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Execution;

public interface IWorkItemHandler
{
    Task<ExecutionResult> HandleWorkItemAsync(WorkItem workItem, IWorkflowExecutionContext ctx);
}
