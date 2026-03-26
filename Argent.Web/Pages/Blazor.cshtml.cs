using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages
{
    public class BlazorModel : PageModel
    {

        public string RawJsonString = "{\r\n  \"Type\": \"Row\",\r\n  \"Layout\": { \"CssClass\": \"p-4 shadow-sm border rounded\" },\r\n  \"Children\": [\r\n    {\r\n      \"Type\": \"CheckboxField\",\r\n      \"Id\": \"sub_check\",\r\n      \"Label\": \"Subscribe to our Newsletter?\",\r\n      \"DataKey\": \"isSubscribed\"\r\n    },\r\n    {\r\n      \"Type\": \"TextField\",\r\n      \"Id\": \"email_input\",\r\n      \"Label\": \"Email Address\",\r\n      \"DataKey\": \"email\",\r\n      \"Logic\": { \"VisibleIf\": \"isSubscribed == true\" },\r\n      \"Validation\": { \"IsRequired\": true }\r\n    },\r\n    {\r\n      \"Type\": \"DropdownField\",\r\n      \"Id\": \"freq_input\",\r\n      \"Label\": \"Send Frequency\",\r\n      \"DataKey\": \"frequency\",\r\n      \"Logic\": { \"VisibleIf\": \"isSubscribed == true\" },\r\n      \"DataProvider\": { \"DataSource\": \"Static\" }\r\n    }\r\n  ]\r\n}";
        
        }
    }
