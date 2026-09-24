using Argent.Core.Forms;
using Argent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Forms.Stores;

public class EfFormDataStore(IDbContextFactory<ArgentDbContext> _dbFactory) : IFormDataStore
{
    public async Task<Dictionary<string, object?>> GetCustomDataAsync(Guid recordId, Guid formId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var row = await db.FormCustomData.AsNoTracking()
            .FirstOrDefaultAsync(c => c.RecordId == recordId && c.FormId == formId);
        return row?.Values ?? [];
    }

    public async Task SaveCustomDataAsync(Guid recordId, Guid formId, Dictionary<string, object?> data)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.FormCustomData
            .FirstOrDefaultAsync(c => c.RecordId == recordId && c.FormId == formId);

        if (existing != null)
        {
            existing.Values = data;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.FormCustomData.Add(new FormCustomData
            {
                RecordId = recordId,
                FormId = formId,
                Values = data
            });
        }

        await db.SaveChangesAsync();
    }
}
