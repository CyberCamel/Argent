using System.Security.Claims;
using Argent.Core.Workflows.Execution;
using Argent.Core.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Tasks;

[Authorize]
public class DetailModel(ITaskInboxService inbox, IWorkflowTaskStore taskStore) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public UserTask TaskItem { get; private set; } = default!;
    public IReadOnlyList<TaskActionDescriptor> Actions { get; private set; } = [];
    public bool CanClaim { get; private set; }
    public bool CanRelease { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var task = await FindAccessibleTaskAsync(cancellationToken);
        if (task is null) return NotFound();
        TaskItem = task;
        var (userId, roles) = Identity();
        CanClaim = task.State == UserTaskState.Pending && task.AssignedTo is null;
        CanRelease = task.State == UserTaskState.Pending &&
            string.Equals(task.AssignedTo, userId, StringComparison.OrdinalIgnoreCase);
        Actions = await taskStore.GetTaskActionDescriptorsAsync(task.InstanceId, task.NodeId);
        return Page();
    }

    public async Task<IActionResult> OnPostClaimAsync(CancellationToken cancellationToken)
    {
        var task = await FindAccessibleTaskAsync(cancellationToken);
        if (task is null) return NotFound();
        var (userId, roles) = Identity();
        try
        {
            await inbox.ClaimAsync(Id, userId, roles, cancellationToken);
            return RedirectToPage("/Tasks/Detail", new { id = Id });
        }
        catch (InvalidOperationException exception)
        {
            TempData["TaskError"] = exception.Message;
            return RedirectToPage("/Tasks/Detail", new { id = Id });
        }
    }

    public async Task<IActionResult> OnPostReleaseAsync(CancellationToken cancellationToken)
    {
        var task = await FindAccessibleTaskAsync(cancellationToken);
        if (task is null) return NotFound();
        var (userId, _) = Identity();
        try
        {
            await inbox.ReleaseAsync(Id, userId, cancellationToken);
            return RedirectToPage("/Tasks/Index");
        }
        catch (InvalidOperationException exception)
        {
            TempData["TaskError"] = exception.Message;
            return RedirectToPage("/Tasks/Detail", new { id = Id });
        }
    }

    public async Task<IActionResult> OnPostCompleteAsync(string? action, CancellationToken cancellationToken)
    {
        var task = await FindAccessibleTaskAsync(cancellationToken);
        if (task is null || task.FormId.HasValue) return NotFound();
        var (userId, roles) = Identity();
        await inbox.CompleteTaskAsync(Id, userId, roles, action, cancellationToken);
        return RedirectToPage("/Tasks/Index");
    }

    private async Task<UserTask?> FindAccessibleTaskAsync(CancellationToken cancellationToken)
    {
        var (userId, roles) = Identity();
        return (await inbox.GetTasksForUserAsync(userId, roles, UserTaskState.Pending, cancellationToken))
            .FirstOrDefault(task => task.Id == Id);
    }

    private (string UserId, List<string> Roles) Identity() =>
        (User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
         User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList());
}
