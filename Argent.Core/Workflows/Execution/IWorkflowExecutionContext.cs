using Argent.Core.Workflows.Execution;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Execution;

public interface IWorkflowExecutionContext
{
    WorkflowInstance Instance { get; }
    IDictionary<string, object> Variables { get; }
}
