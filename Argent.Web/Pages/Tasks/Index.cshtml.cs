using System.Security.Claims;
using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.Tasks;

[Authorize(Policy = "PbacTaskView")]
public class IndexModel(IUserTaskManager _taskManager) : PageModel
{
    public List<UserTask> Tasks { get; set; } = [];

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        Tasks = await _taskManager.GetTasksForUserAsync(userId, roles, UserTaskState.Pending);
    }
}
