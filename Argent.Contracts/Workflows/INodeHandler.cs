using System;
using System.Collections.Generic;
using System.Text;
using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;

namespace Argent.Contracts.Workflows
{
    internal interface INodeHandler<in TNode> where TNode : NodeBase
    {
        Task ExecuteAsync(TNode node, IWorkflowExecutionContext ctx);
    }
}
