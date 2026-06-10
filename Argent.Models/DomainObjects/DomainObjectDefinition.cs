namespace Argent.Models.DomainObjects;

[Serializable]
public class DomainObjectDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    
    public Dictionary<string, object> Properties { get; set; }
    
}
