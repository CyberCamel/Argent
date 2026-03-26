namespace Argent.Web.ViewModels
{
    public class UserViewModel
    {
        public string? UserName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public List<string> Roles { get; set; } = [];
    }
}
