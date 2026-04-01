using Argent.Contracts.Workflows;
using Argent.Core.Attributes;
using Argent.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Argent.Logic.Forms
{
    public class ArgentWorkflowNodeRegistry : IWorkflowNodeRegistry
    {
        private readonly List<NodeMetadata> _cache;

        public ArgentWorkflowNodeRegistry()
        {
            // Scan the Assembly where Node lives
            _cache = typeof(Node).Assembly.GetTypes()
                .Select(t => new { Type = t, Attr = t.GetCustomAttribute<WorkflowCanvasElementAttribute>() })
                .Where(x => x.Attr != null)
                .Select(x => new NodeMetadata
                {
                    TypeIdentifier = x.Type.Name,
                    DisplayName = x.Attr!.DisplayName,
                    Icon = x.Attr.Icon,
                    Category = x.Attr.Category,
                    Description = x.Attr.Description
                })
                .ToList();
        }

        public IEnumerable<NodeMetadata> GetRegisteredTypes() => _cache;
        public Type Resolve(string name)
        {
            var metadata = _cache.FirstOrDefault(m => m.TypeIdentifier.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (metadata == null)
                throw new InvalidOperationException($"Workflow node type '{name}' is not registered.");
            return typeof(Node).Assembly.GetType(metadata.TypeIdentifier) ?? throw new InvalidOperationException($"Type '{metadata.TypeIdentifier}' not found in assembly.");
        }
    }

}
