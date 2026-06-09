using Argent.Contracts.Workflows;
using Argent.Models.Attributes;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
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

        private static Contracts.Workflows.PropertyDataType MapDataType(Models.Workflows.Modeler.Enums.PropertyDataType dt) => dt switch
        {
            Models.Workflows.Modeler.Enums.PropertyDataType.Number => Contracts.Workflows.PropertyDataType.Number,
            Models.Workflows.Modeler.Enums.PropertyDataType.Boolean => Contracts.Workflows.PropertyDataType.Boolean,
            Models.Workflows.Modeler.Enums.PropertyDataType.DateTime => Contracts.Workflows.PropertyDataType.DateTime,
            Models.Workflows.Modeler.Enums.PropertyDataType.MultiLineText => Contracts.Workflows.PropertyDataType.MultiLineText,
            Models.Workflows.Modeler.Enums.PropertyDataType.KeyValuePairs => Contracts.Workflows.PropertyDataType.KeyValuePairs,
            Models.Workflows.Modeler.Enums.PropertyDataType.Code => Contracts.Workflows.PropertyDataType.Code,
            _ => Contracts.Workflows.PropertyDataType.Text
        };

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
                    DataType = MapDataType(x.propAttr.DataType),
                })
                .OrderBy(p => p.DisplayName)
                .ToList();

            return new NodeTypeDescriptor
            {
                NodeType = type,
                DisplayName = attr.DisplayName,
                Icon = attr.Icon,
                Category = attr.Category,
                Shape = (Contracts.Workflows.NodeShape)(int)attr.Shape,
                Description = attr.Description,
                CssClass = attr.CssClass,
                DefaultWidth = attr.DefaultWidth,
                DefaultHeight = attr.DefaultHeight,
                Properties = properties
            };
        }

        private static Dictionary<Type, NodeTypeDescriptor> BuildDescriptors()
        {
            var types = new[]
            {
                typeof(StartEvent),
                typeof(EndEvent),
                typeof(SQLActivity),
                typeof(JintActivity),
                typeof(RestActivity),
                typeof(UserActivity),
                typeof(InclusiveGateway),
                typeof(ExclusiveGateway),
            };

            return types.ToDictionary(t => t, FromAttribute);
        }
    }
}
