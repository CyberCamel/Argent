using Argent.Contracts.DomainObjects;
using Argent.Infrastructure.Data;
using Argent.Models.DomainObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.DomainObjects.Records;

public class IndexModel(IDomainObjectDefinitionService _definitions, IDomainObjectStore _store, IDbContextFactory<ArgentDbContext> _factory) : PageModel
{
    [FromRoute] public string Key { get; set; } = "";

    public DomainObjectDefinition Definition { get; set; } = default!;
    public List<DomainRecord> Records { get; set; } = [];
    public int TotalCount { get; set; }

    public async Task<IActionResult> OnGet()
    {
        var def = await _definitions.GetPublishedDefinitionAsync(Key);
        if (def == null) return NotFound();
        Definition = def;

        var result = await _store.QueryAsync(Key);
        Records = result.Records.ToList();
        TotalCount = result.TotalCount;
        return Page();
    }

    public async Task<IActionResult> OnPostDelete(Guid id)
    {
        using var db = _factory.CreateDbContext();
        var domainObj = await db.DomainObjects.AsNoTracking().FirstOrDefaultAsync(o => o.Key == Key);
        if (domainObj != null)
        {
            var record = await db.DomainObjectRecords.FindAsync(id);
            if (record != null && record.DomainObjectId == domainObj.Id)
            {
                db.DomainObjectRecords.Remove(record);
                await db.SaveChangesAsync();
            }
        }
        return RedirectToPage(new { Key });
    }
}
