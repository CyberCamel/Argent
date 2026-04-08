using System;
using System.Collections.Generic;
using System.Text;
using Argent.Contracts.Workflows.Core;
using Argent.Core.Workflows;

namespace Argent.Contracts.Workflows
{
    internal interface INodeHandler<in TNode> where TNode : INode
    {
        Task ExecuteAsync(TNode node, IWorkflowContext workflowContext);
    }
}
