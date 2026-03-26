using Microsoft.AspNetCore.Mvc.RazorPages;
using Argent.Core.Forms.Components;
using Argent.Core.Forms.Components.Base;

namespace Argent.Web.Pages
{
    public class FormDemoModel : PageModel
    {
        public FormDefinition MockForm { get; private set; } = default!;

        public void OnGet()
        {
            // Create a mock form with two ArgentText components
            MockForm = new FormDefinition
            {
                FormId = "demo-form-1",
                Version = 1,
                Title = "Demo Form",
                Components = new List<FormComponent>
                {
                    new FormComponent
                    {
                        Id = "text-field-1",
                        Type = "ArgentText",
                        DataKey = "firstName",
                        Label = "First Name",
                        Description = "Enter your first name",
                        DefaultValue = "John"
                    },
                    new FormComponent
                    {
                        Id = "text-field-2",
                        Type = "ArgentText",
                        DataKey = "lastName",
                        Label = "Last Name",
                        Description = "Enter your last name",
                        DefaultValue = "Doe"
                    }
                }
            };
        }
    }
}
