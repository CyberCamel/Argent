using Argent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Forms;

public class FormListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public class IndexModel(ArgentDbContext _ctx) : PageModel
{
    public List<FormListItem> Forms { get; set; } = [];

    public async Task<IActionResult> OnGet()
    {
        Forms = await _ctx.FormDesigns
            .OrderByDescending(f => f.UpdatedAt)
            .Select(f => new FormListItem
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                UpdatedAt = f.UpdatedAt
            })
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostDelete(Guid id)
    {
        var doc = await _ctx.FormDesigns.FindAsync(id);
        if (doc != null)
        {
            _ctx.FormDesigns.Remove(doc);
            await _ctx.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
