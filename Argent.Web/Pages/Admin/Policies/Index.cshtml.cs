using Argent.Infrastructure.Data;
using Argent.Models.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Admin.Policies;

[Authorize(Policy = "SuperAdminOnly")]
public class IndexModel(ArgentDbContext _ctx) : PageModel
{
    public List<PolicyDocument> Policies { get; set; } = [];

    public async Task OnGetAsync()
    {
        Policies = await _ctx.PolicyDocuments
            .OrderByDescending(p => p.Priority)
            .ToListAsync();
    }
}
