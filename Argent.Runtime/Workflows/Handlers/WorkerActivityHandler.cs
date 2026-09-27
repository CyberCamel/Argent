using Argent.Core.Workers;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.Execution;
using Argent.Runtime.DataSources;
using Argent.Runtime.Workers;

namespace Argent.Runtime.Workflows.Handlers;

/// <summary>
/// Enqueues work for an external worker and parks the token until the worker responds.
/// The engine never learns what the work is: it sends a worker name, a subject string and
/// key/value parameters, and turns whatever key/value outputs come back into process variables.
/// </summary>
public class WorkerActivityHandler(
    IWorkerRequestQueue queue,
    IWorkerRegistry registry) : INodeHandler
{
    public Type HandledNodeType => typeof(WorkerActivity);

    public async Task<NodeResult> ExecuteAsync(NodeBase node, ITokenExecutionContext ctx, CancellationToken ct)
    {
        var activity = (WorkerActivity)node;

        // TokenRunner branches on ResultType, not on Success, so a failure must say so explicitly
        // or the token would move on as if the task had succeeded.
        if (string.IsNullOrWhiteSpace(activity.WorkerName))
            return Failed("The worker activity has no worker name configured.");

        if (string.IsNullOrWhiteSpace(activity.Subject))
            return Failed($"The worker activity on worker '{activity.WorkerName}' has no subject configured.");

        var existing = await queue.GetForTokenAsync(ctx.TokenId, ct);

        if (existing == null)
        {
            var known = await registry.IsRegisteredAsync(activity.WorkerName, ct);

            // A typo in the worker name is a design error and should surface as a failed node, not
            // as a workflow that waits forever. Opt in to waiting when the worker is expected to
            // appear later, such as during local development or a staggered deploy.
            if (!known && !activity.WaitForWorker)
            {
                return Failed(
                    $"No worker named '{activity.WorkerName}' is registered. " +
                    "Register the worker, or enable 'Wait for worker' on this node to park until it appears.");
            }

            var request = new WorkerRequest
            {
                Id = Guid.NewGuid(),
                InstanceId = ctx.InstanceId,
                TokenId = ctx.TokenId,
                WorkItemId = ctx.WorkItemId,
                NodeId = ctx.NodeId,
                WorkerName = activity.WorkerName,
                Subject = activity.Subject,
                Parameters = WorkerRequestSerialization.WriteKeyValues(BuildParameters(activity, ctx)),
                State = WorkerRequestState.Pending,
                Priority = 0,
                MaxAttempts = (byte)Math.Clamp(activity.MaxAttempts <= 0 ? 1 : activity.MaxAttempts, 1, 10),
                TimeoutSeconds = Math.Clamp(activity.TimeoutSeconds <= 0 ? 900 : activity.TimeoutSeconds, 1, 86_400),
                CreatedAt = DateTime.UtcNow
            };

            await queue.EnqueueAsync(request, ct);
            return new NodeResult(true, ResultType: NodeResultType.Waiting);
        }

        return existing.State switch
        {
            WorkerRequestState.Succeeded => BuildSuccess(existing),
            WorkerRequestState.Failed => Failed(DescribeFailure(existing, "reported a failure")),
            WorkerRequestState.TimedOut => Failed(DescribeFailure(existing, "timed out")),
            WorkerRequestState.Cancelled => Failed(DescribeFailure(existing, "was cancelled")),
            _ => new NodeResult(true, ResultType: NodeResultType.Waiting)
        };
    }

    private static NodeResult Failed(string message) => new(false, message, ResultType: NodeResultType.Failed);

    private static NodeResult BuildSuccess(WorkerRequest request)
    {
        var outputs = request.GetOutputs();

        // Outputs become process variables under the keys the worker chose, so downstream nodes
        // and gateway conditions can read them like any other workflow variable.
        var variables = new Dictionary<string, object?>(outputs.Count);
        foreach (var (key, value) in outputs)
            variables[key] = value;

        return new NodeResult(true, OutputVariables: variables);
    }

    private static string DescribeFailure(WorkerRequest request, string outcome)
    {
        var message = $"Worker '{request.WorkerName}' subject '{request.Subject}' {outcome}.";
        return string.IsNullOrWhiteSpace(request.ErrorMessage)
            ? message
            : $"{message} {request.ErrorMessage}";
    }

    private static Dictionary<string, string> BuildParameters(WorkerActivity activity, ITokenExecutionContext ctx)
    {
        var variables = ctx.Variables.Snapshot()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var parameter in activity.Parameters)
        {
            if (parameter == null || string.IsNullOrWhiteSpace(parameter.Key)) continue;
            parameters[parameter.Key] = TokenTemplate.Apply(parameter.Value, variables);
        }

        return parameters;
    }
}
