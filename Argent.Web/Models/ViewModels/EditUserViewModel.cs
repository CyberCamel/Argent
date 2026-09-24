namespace Argent.Web.Models.ViewModels;

public class EditUserViewModel
{
    public string UserName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public List<string> Roles { get; set; } = [];
    public List<string> AvailableRoles { get; set; } = [];
}
