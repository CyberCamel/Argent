using Argent.Core.Workflows;
using Argent.Core.Attributes;
using System.Reflection;

namespace Argent.Runtime.Workflows
{
    public class ArgentWorkflowNodeRegistry : IWorkflowNodeRegistry
    {
        private readonly Dictionary<Type, NodeTypeDescriptor> _descriptors;

        public ArgentWorkflowNodeRegistry()
        {
            _descriptors = BuildDescriptors();
        }

        public IEnumerable<NodeTypeDescriptor> GetRegisteredTypes() => _descriptors.Values;

        public Type? Resolve(string name)
            => _descriptors.Values.FirstOrDefault(d =>
                d.NodeType.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.NodeType;

        public NodeTypeDescriptor? GetDescriptor(Type type)
            => _descriptors.TryGetValue(type, out var desc) ? desc : null;

        private static NodeTypeDescriptor FromAttribute(Type type)
        {
            var attr = type.GetCustomAttribute<WorkflowCanvasElementAttribute>()!;
            if (attr == null)
                throw new InvalidOperationException($"Type {type.Name} is missing [WorkflowCanvasElement] attribute");

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (p, propAttr: p.GetCustomAttribute<NodePropertyAttribute>()))
                .Where(x => x.propAttr != null)
                .Select(x => new PropertyDescriptor
                {
                    PropertyName = x.p.Name,
                    DisplayName = x.propAttr.Name,
                    Description = x.propAttr.Description,
                    Required = x.propAttr.Required,
                    DataType = x.propAttr.DataType,
                })
                .OrderBy(p => p.DisplayName)
                .ToList();

            return new NodeTypeDescriptor
            {
                NodeType = type,
                DisplayName = attr.DisplayName,
                Icon = attr.Icon,
                Category = attr.Category,
                Shape = attr.Shape,
                Description = attr.Description,
                CssClass = attr.CssClass,
                DefaultWidth = attr.DefaultWidth,
                DefaultHeight = attr.DefaultHeight,
                Properties = properties
            };
        }

        private static Dictionary<Type, NodeTypeDescriptor> BuildDescriptors()
        {
            var nodeBaseType = typeof(NodeBase);
            var attrType = typeof(WorkflowCanvasElementAttribute);

            var types = nodeBaseType.Assembly.GetTypes()
                .Where(t => !t.IsAbstract && nodeBaseType.IsAssignableFrom(t) && t.IsDefined(attrType, inherit: false));

            return types.ToDictionary(t => t, FromAttribute);
        }
    }
}
