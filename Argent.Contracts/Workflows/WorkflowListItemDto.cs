namespace Argent.Contracts.Workflows;

public record WorkflowListItemDto
{
    public Guid Id;
    public string Name;
    public string Description;
    public int NumberOfInstances;
};