using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
using Argent.Models.Workflows.Execution;

namespace Argent.Runtime.Workflows.Handlers;

public class UserActivityHandler : INodeHandler
{
    private readonly IUserTaskManager _taskManager;

    public Type HandledNodeType => typeof(UserActivity);

    public UserActivityHandler(IUserTaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public async Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var activity = (UserActivity)node;

        var existingTask = await _taskManager.GetTaskByTokenAsync(ctx.TokenId, ct);

        if (existingTask == null)
        {
            // First execution — create the user task
            DateTime? dueDate = activity.UX switch
            {
                TaskExperience t => DateTime.UtcNow.Add(t.Timeout),
                _ => null
            };

            await _taskManager.CreateTaskAsync(
                ctx.InstanceId, ctx.TokenId, ctx.NodeId, dueDate, ct);

            return new NodeResult(true, ResultType: NodeResultType.Waiting);
        }

        if (existingTask.State == UserTaskState.Completed)
        {
            return new NodeResult(true);
        }

        if (existingTask.DueDate != null && existingTask.DueDate <= DateTime.UtcNow)
        {
            return new NodeResult(true);
        }

        return new NodeResult(true, ResultType: NodeResultType.Waiting);
    }
}
