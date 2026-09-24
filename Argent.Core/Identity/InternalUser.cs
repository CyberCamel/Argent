namespace Argent.Core.Identity;

public class InternalUser : Person
{
    public bool IsManager { get; set; }
    public string? Department { get; set; }

}
