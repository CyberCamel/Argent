using Argent.Contracts.Workflows;
using Argent.Models.Attributes;
using Argent.Models.Workflows;

using System.Reflection;


namespace Argent.Runtime.Workflows
{
    public class ArgentWorkflowNodeRegistry : IWorkflowNodeRegistry
    {
        private readonly List<NodeMetadata> _cache;

        public ArgentWorkflowNodeRegistry()
        {
            // Scan the Assembly where Node lives
            _cache = [.. typeof(NodeBase).Assembly.GetTypes()
                .Select(t => new { Type = t, Attr = t.GetCustomAttribute<WorkflowCanvasElementAttribute>() })
                .Where(x => x.Attr != null)
                .Select(x => new NodeMetadata
                {
                    Type = x.Type,
                    DisplayName = x.Attr!.DisplayName,
                    Icon = x.Attr.Icon,
                    Category = x.Attr.Category,
                    Description = x.Attr.Description,
                    CssClass = x.Attr.CssClass,
                    Shape=x.Attr.Shape
                })];
        }

        public IEnumerable<NodeMetadata> GetRegisteredTypes() => _cache;
        public Type Resolve(string name)
        {
            var metadata = _cache.FirstOrDefault(m => m.Type.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (metadata == null)
                throw new InvalidOperationException($"Workflow node type '{name}' is not registered.");
            return typeof(NodeBase).Assembly.GetType(metadata.Type.Name) ?? throw new InvalidOperationException($"Type '{metadata.Type.Name}' not found in assembly.");
        }
    }

}
