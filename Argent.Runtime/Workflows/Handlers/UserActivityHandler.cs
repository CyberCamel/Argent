using Argent.Contracts.Workflows.Execution;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Activities;
using Argent.Models.Workflows.Execution;
using Argent.Runtime.Workflows.Execution;
using NCalc;
using System.Text.Json;

namespace Argent.Runtime.Workflows.Handlers;

public class UserActivityHandler(
    IUserTaskManager taskManager,
    IWorkflowAudienceResolver audienceResolver) : INodeHandler
{
    public Type HandledNodeType => typeof(UserActivity);

    public async Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var activity = (UserActivity)node;

        var existingTask = await taskManager.GetTaskByTokenAsync(ctx.TokenId, ct);

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

            var task = await taskManager.CreateTaskAsync(
                ctx.InstanceId, ctx.TokenId, ctx.NodeId, dueDate,
                title: activity.TaskTitle,
                description: activity.TaskDescription,
                priority: activity.TaskPriority,
                assigneeExpression: assignee,
                formId: formId,
                formData: null,
                ct: ct);

            // Snapshot the swimlane audience into CandidateUsers at task creation time.
            if (activity.LaneRoleId is Guid roleId)
            {
                var userIds = await audienceResolver.ResolveAsync(ctx.InstanceId, roleId, ct);
                if (userIds.Count > 0)
                    await taskManager.SetCandidateUsersAsync(task.Id, JsonSerializer.Serialize(userIds), ct);
            }

            return new NodeResult(true, ResultType: NodeResultType.Waiting);
        }

        if (existingTask.State == UserTaskState.Completed)
        {
            if (!string.IsNullOrEmpty(existingTask.ResultData))
            {
                var match = ctx.CandidateTargets.FirstOrDefault(t => t.Label == existingTask.ResultData);
                if (match != null)
                    return new NodeResult(true, ExplicitTargetNodeIds: [match.NodeId]);
            }
            return new NodeResult(true);
        }

        if (existingTask.DueDate != null && existingTask.DueDate <= DateTime.UtcNow)
            return new NodeResult(true);

        return new NodeResult(true, ResultType: NodeResultType.Waiting);
    }
}
