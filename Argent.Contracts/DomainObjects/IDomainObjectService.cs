using Argent.Models.DomainObjects;

namespace Argent.Contracts.DomainObjects;

public class DomainObjectSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public interface IDomainObjectService
{
    Task<List<DomainObjectSummary>> GetSummariesAsync();
    Task<DomainObjectDefinition?> GetDefinitionAsync(Guid id);
}
