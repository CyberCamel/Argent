using System.ComponentModel.DataAnnotations;

namespace Argent.Web.ViewModels
{
    public class CreateUserViewModel
    {
        [Required(ErrorMessage = "RequiredField")]
        [Display(Name = "Username")]
        public string? UserName { get; set; }

        [Required(ErrorMessage = "RequiredField")]
        [Display(Name = "First name")]
        public string? FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "RequiredField")]
        [Display(Name = "Last name")]
        public string? LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "RequiredField")]
        [EmailAddress(ErrorMessage = "Please enter a valid email (e.g. name@domain.com)")]
        [Display(Name = "Email address")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "RequiredField")]
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [Required(ErrorMessage = "RequiredField")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The passwords do not match.")]
        public string? ConfirmPassword { get; set; }
    }
}