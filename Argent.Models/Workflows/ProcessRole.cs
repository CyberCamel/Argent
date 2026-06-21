namespace Argent.Models.Workflows;

public class ProcessRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
}
