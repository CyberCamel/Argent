using System.Text.Json;
using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
using Argent.Models.Workflows.Execution;
using NCalc;

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
            DateTime? dueDate = activity.UX switch
            {
                TaskExperience t => DateTime.UtcNow.Add(t.Timeout),
                _ => null
            };

            string? assignee = null;
            if (!string.IsNullOrWhiteSpace(activity.AssigneeExpression))
            {
                var expr = new Expression(activity.AssigneeExpression);
                foreach (var kvp in ctx.Variables.Snapshot())
                    expr.Parameters[kvp.Key] = kvp.Value;
                var result = expr.Evaluate();
                if (result != null)
                    assignee = result.ToString();
            }

            Guid? formId = activity.UX switch
            {
                FormExperience f => f.FormId,
                _ => null
            };

            await _taskManager.CreateTaskAsync(
                ctx.InstanceId, ctx.TokenId, ctx.NodeId, dueDate,
                title: activity.TaskTitle,
                description: activity.TaskDescription,
                priority: activity.TaskPriority,
                assigneeExpression: assignee,
                candidateRoles: activity.CandidateRoles,
                formId: formId,
                formData: null,
                ct: ct);

            return new NodeResult(true, ResultType: NodeResultType.Waiting);
        }

        if (existingTask.State == UserTaskState.Completed)
        {
            Dictionary<string, object?>? outputVars = null;

            if (!string.IsNullOrEmpty(existingTask.ResultData))
            {
                try
                {
                    outputVars = JsonSerializer.Deserialize<Dictionary<string, object?>>(existingTask.ResultData);
                }
                catch
                {
                    outputVars = new Dictionary<string, object?> { ["resultData"] = existingTask.ResultData };
                }
            }

            return new NodeResult(true, OutputVariables: outputVars?.AsReadOnly());
        }

        if (existingTask.DueDate != null && existingTask.DueDate <= DateTime.UtcNow)
        {
            return new NodeResult(true);
        }

        return new NodeResult(true, ResultType: NodeResultType.Waiting);
    }
}
