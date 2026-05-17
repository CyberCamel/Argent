using Argent.Models.Workflows.Execution;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Workflows.Execution;

public interface IWorkflowExecutionContext
{
    WorkflowInstance Instance { get; }
    IDictionary<string, object> Variables { get; }
}
