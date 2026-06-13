using Argent.Contracts.DataSources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.DataSources;

[Authorize(Roles = "SuperAdmin")]
public class IndexModel(IDataSourceCatalog _catalog) : PageModel
{
    public List<DataSourceSummary> DataSources { get; set; } = [];

    public async Task<IActionResult> OnGet()
    {
        DataSources = await _catalog.GetSummariesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDelete(Guid id)
    {
        await _catalog.DeleteAsync(id);
        return RedirectToPage();
    }
}
