using Argent.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Workflows
{
    public interface IWorkflowNodeRegistry
    {
        public Type Resolve(string name);

        /// <summary>
        /// Gets all registered workflow node types. This can be used for tooling, such as a workflow designer, to display available nodes.
        /// </summary>
        public IEnumerable<NodeMetadata> GetRegisteredTypes();
    }
}
