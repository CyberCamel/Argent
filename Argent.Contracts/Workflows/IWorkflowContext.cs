using Argent.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Workflows.Core;

internal interface IWorkflowContext
{
    public Guid WorkflowInstanceId { get; }
    public Dictionary<string, object> Variables { get; }
    public List<Token> Tokens { get; }



}
