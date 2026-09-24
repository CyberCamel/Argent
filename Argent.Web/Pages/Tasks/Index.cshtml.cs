using System.Security.Claims;
using Argent.Core.Workflows.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Tasks;

[Authorize]
public class IndexModel(ITaskInboxService _taskManager) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public TaskListScope Scope { get; set; } = TaskListScope.Active;
    [BindProperty(SupportsGet = true)] public TaskListSort Sort { get; set; } = TaskListSort.DueDate;
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;
    public TaskListPage Result { get; private set; } = new([], 0, 1, 20);
    [TempData] public string? TaskError { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var (userId, roles) = Identity();
        Result = await _taskManager.QueryForUserAsync(userId, roles, new TaskListRequest
        {
            Search = Search,
            Scope = Scope,
            Sort = Sort,
            Page = PageNumber,
            PageSize = 20
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostClaimAsync(Guid id, CancellationToken cancellationToken)
    {
        var (userId, roles) = Identity();
        try { await _taskManager.ClaimAsync(id, userId, roles, cancellationToken); }
        catch (InvalidOperationException exception) { TaskError = exception.Message; }
        return RedirectToPage("/Tasks/Index", new { Search, Scope, Sort, p = PageNumber });
    }

    public async Task<IActionResult> OnPostReleaseAsync(Guid id, CancellationToken cancellationToken)
    {
        var (userId, _) = Identity();
        try { await _taskManager.ReleaseAsync(id, userId, cancellationToken); }
        catch (InvalidOperationException exception) { TaskError = exception.Message; }
        return RedirectToPage("/Tasks/Index", new { Search, Scope, Sort, p = PageNumber });
    }

    private (string UserId, List<string> Roles) Identity() =>
        (User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
         User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList());
}
