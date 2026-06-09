namespace Argent.Contracts.Workflows
{
    public interface IWorkflowNodeRegistry
    {
        Type? Resolve(string name);
        IEnumerable<NodeTypeDescriptor> GetRegisteredTypes();
        NodeTypeDescriptor? GetDescriptor(Type type);
    }
}
