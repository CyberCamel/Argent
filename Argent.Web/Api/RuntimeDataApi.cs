using System.Security.Claims;
using Argent.Core;
using Argent.Core.Forms;
using Argent.Core.Forms.Protocol.V2;
using Argent.Core.DomainObjects;
using Argent.Core.DomainObjects.Querying;
using Argent.Core.Authorization;
using Argent.Core.Workflows.Execution;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Argent.Web.Api;

/// <summary>
/// Runtime data endpoints consumed by browser clients. The identity used for writes and policy
/// decisions is always taken from the authenticated request, never from the payload.
/// </summary>
public static class RuntimeDataApi
{
    public static IEndpointRouteBuilder MapRuntimeDataApi(this IEndpointRouteBuilder app)
    {
        // ── Domain Records ─────────────────────────────────────────────────────

        app.MapGet("/api/records/{objectKey}/{id:guid}", async (string objectKey, Guid id, IDomainObjectStore store) =>
            await store.GetAsync(objectKey, id) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapPost("/api/records/{objectKey}/query", async (string objectKey, [FromBody] DomainQuery? query, IDomainObjectStore store) =>
            Results.Ok(await store.QueryAsync(objectKey, query)));

        app.MapPost("/api/records/{objectKey}", async (string objectKey, [FromBody] Dictionary<string, object?> values, IDomainObjectStore store, HttpContext ctx) =>
            await GuardValidation(async () => Results.Ok(await store.CreateAsync(objectKey, values, ctx.User?.Identity?.Name))));

        app.MapPut("/api/records/{objectKey}/{id:guid}", async (string objectKey, Guid id, [FromBody] Dictionary<string, object?> values, IDomainObjectStore store, HttpContext ctx) =>
            await GuardValidation(async () => Results.Ok(await store.UpdateAsync(objectKey, id, values, ctx.User?.Identity?.Name))));

        app.MapPost("/api/records/{objectKey}/upsert", async (string objectKey, [FromBody] DomainRecord record, IDomainObjectStore store, HttpContext ctx) =>
            await GuardValidation(async () => Results.Ok(await store.UpsertAsync(objectKey, record, ctx.User?.Identity?.Name))));

        app.MapDelete("/api/records/{objectKey}/{id:guid}", async (string objectKey, Guid id, IDomainObjectStore store) =>
        {
            await store.DeleteAsync(objectKey, id);
            return Results.NoContent();
        });

        app.MapPost("/api/records/{objectKey}/options", async (string objectKey, [FromBody] OptionsRequest req, IDomainObjectStore store) =>
            Results.Ok(await store.GetOptionsAsync(objectKey, req.ValueField, req.LabelField, req.DataSourceIndex, req.Query)));

        app.MapPost("/api/records/{objectKey}/data-sources/{index:int}/query",
            async (string objectKey, int index, [FromBody] DomainQuery? query, IDomainObjectStore store) =>
                Results.Ok(await store.QueryDataSourceAsync(objectKey, index, query)));

        // ── Public Form Runtime ───────────────────────────────────────────────

        app.MapGet("/api/runtime/forms/{formDesignId:guid}/bootstrap",
            async (Guid formDesignId, IFormRuntimeService runtime, IStringLocalizer<SharedResource> localizer,
                CancellationToken cancellationToken) =>
                await runtime.BootstrapAsync(formDesignId, cancellationToken: cancellationToken) is { } bootstrap
                    ? Results.Ok(Localize(bootstrap, localizer))
                    : Results.NotFound())
            .AllowAnonymous();

        app.MapPost("/api/runtime/forms/{formDesignId:guid}/submit",
            async (Guid formDesignId, [FromBody] FormSubmitRequest request, IFormRuntimeService runtime,
                IAntiforgery antiforgery, HttpContext context, CancellationToken cancellationToken) =>
            {
                if (request.RecordId.HasValue)
                    return Results.BadRequest(new { error = "Public form submissions cannot select an existing record." });
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest(new { error = "The submission security token is missing or invalid." });
                }

                var submission = await runtime.SubmitAsync(
                    formDesignId, request, context.User.Identity?.Name, cancellationToken);
                return submission.IsValid
                    ? Results.Ok(submission.Result)
                    : Results.UnprocessableEntity(new { errors = submission.Errors });
            })
            .AllowAnonymous();

        app.MapGet("/api/runtime/workflows/{workflowId:guid}/start-form/bootstrap",
            async (Guid workflowId, IWorkflowTaskStore tasks, IFormRuntimeService runtime,
                IStringLocalizer<SharedResource> localizer, CancellationToken cancellationToken) =>
            {
                var formDesignId = await tasks.GetStartFormIdAsync(workflowId);
                if (formDesignId is null) return Results.NotFound();
                return await runtime.BootstrapAsync(formDesignId.Value, cancellationToken: cancellationToken) is { } bootstrap
                    ? Results.Ok(Localize(bootstrap, localizer))
                    : Results.NotFound();
            })
            .RequireAuthorization();

        app.MapPost("/api/runtime/workflows/{workflowId:guid}/start-form/submit",
            async (Guid workflowId, [FromBody] FormSubmitRequest request, IWorkflowTaskStore tasks,
                IFormRuntimeService runtime, IWorkflowInstanceService workflows,
                IPolicyDecisionService policies, IDomainObjectStore domainObjects,
                IAntiforgery antiforgery, HttpContext context, CancellationToken cancellationToken) =>
            {
                if (request.RecordId.HasValue)
                    return Results.BadRequest(new { error = "Workflow start submissions cannot select an existing record." });
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest(new { error = "The submission security token is missing or invalid." });
                }

                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                var roles = context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList();
                var decision = await policies.EvaluateAsync(userId, roles, "Workflow",
                    new Dictionary<string, object?> { ["id"] = workflowId.ToString() },
                    ResourceActions.Workflow.Run);
                if (decision != PolicyDecision.Allow) return Results.Forbid();

                var formDesignId = await tasks.GetStartFormIdAsync(workflowId);
                if (formDesignId is null) return Results.NotFound();
                var submission = await runtime.SubmitAsync(
                    formDesignId.Value, request, context.User.Identity?.Name, cancellationToken);
                if (!submission.IsValid)
                    return Results.UnprocessableEntity(new { errors = submission.Errors });

                if (submission.Result!.IsReplay)
                {
                    return submission.Result.WorkflowInstanceId is { } existingInstanceId
                        ? Results.Ok(new { submission.Result.RecordId, instanceId = existingInstanceId })
                        : Results.Conflict(new { error = "The previous workflow start has not completed. Please try again shortly." });
                }

                var bootstrap = await runtime.BootstrapAsync(formDesignId.Value, cancellationToken: cancellationToken);
                if (bootstrap is null) return Results.NotFound();
                try
                {
                    var instanceId = await workflows.StartAsync(
                        workflowId, submission.Result!.RecordId, null, cancellationToken);
                    await runtime.CompleteWorkflowStartAsync(request.SubmissionId, instanceId, cancellationToken);
                    return Results.Ok(new { submission.Result.RecordId, instanceId });
                }
                catch
                {
                    await domainObjects.DeleteAsync(
                        bootstrap.Definition.ObjectKey, submission.Result!.RecordId);
                    await runtime.DiscardSubmissionAsync(request.SubmissionId, cancellationToken);
                    throw;
                }
            })
            .RequireAuthorization();

        app.MapGet("/api/runtime/tasks",
            async ([FromQuery] string? search, [FromQuery] TaskListScope scope,
                [FromQuery] TaskListSort sort, [FromQuery] int page, [FromQuery] int pageSize,
                ITaskInboxService inbox, HttpContext context, CancellationToken cancellationToken) =>
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                var roles = context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList();
                return Results.Ok(await inbox.QueryForUserAsync(userId, roles, new TaskListRequest
                {
                    Search = search,
                    Scope = scope,
                    Sort = sort,
                    Page = page == 0 ? 1 : page,
                    PageSize = pageSize == 0 ? 20 : pageSize
                }, cancellationToken));
            })
            .RequireAuthorization();

        app.MapPost("/api/runtime/tasks/{taskId:guid}/claim",
            async (Guid taskId, ITaskInboxService inbox, IAntiforgery antiforgery,
                HttpContext context, CancellationToken cancellationToken) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                var roles = context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList();
                try
                {
                    await inbox.ClaimAsync(taskId, userId, roles, cancellationToken);
                    return Results.NoContent();
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { error = exception.Message });
                }
            })
            .RequireAuthorization();

        app.MapPost("/api/runtime/tasks/{taskId:guid}/release",
            async (Guid taskId, ITaskInboxService inbox, IAntiforgery antiforgery,
                HttpContext context, CancellationToken cancellationToken) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                try
                {
                    await inbox.ReleaseAsync(taskId, userId, cancellationToken);
                    return Results.NoContent();
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { error = exception.Message });
                }
            })
            .RequireAuthorization();

        app.MapGet("/api/runtime/tasks/{taskId:guid}/form/bootstrap",
            async (Guid taskId, ITaskInboxService inbox, IWorkflowTaskStore tasks,
                IWorkflowInstanceService workflows, IFormRuntimeService runtime,
                IStringLocalizer<SharedResource> localizer, HttpContext context, CancellationToken cancellationToken) =>
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                var roles = context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList();
                var task = await inbox.GetAsync(taskId, cancellationToken);
                if (task is null || task.State != UserTaskState.Pending ||
                    !await CanAccessTaskAsync(inbox, taskId, userId, roles, cancellationToken))
                    return Results.NotFound();
                if (task.FormId is null) return Results.NotFound();

                var snapshot = await workflows.GetStateAsync(task.InstanceId, cancellationToken);
                var bootstrap = await runtime.BootstrapAsync(
                    task.FormId.Value, snapshot.RecordId, cancellationToken);
                if (bootstrap is null) return Results.NotFound();
                Localize(bootstrap, localizer);
                var actions = await tasks.GetTaskActionDescriptorsAsync(task.InstanceId, task.NodeId);
                return Results.Ok(new
                {
                    bootstrap.FormDesignId,
                    bootstrap.FormVersionId,
                    bootstrap.Definition,
                    bootstrap.InitialValues,
                    bootstrap.Messages,
                    actions
                });
            })
            .RequireAuthorization();

        app.MapPost("/api/runtime/tasks/{taskId:guid}/form/submit",
            async (Guid taskId, [FromBody] FormSubmitRequest request, ITaskInboxService inbox,
                IWorkflowInstanceService workflows, IFormRuntimeService runtime,
                IAntiforgery antiforgery, HttpContext context, CancellationToken cancellationToken) =>
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest(new { error = "The submission security token is missing or invalid." });
                }

                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                var roles = context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList();
                var task = await inbox.GetAsync(taskId, cancellationToken);
                if (task is null || task.State != UserTaskState.Pending ||
                    !await CanAccessTaskAsync(inbox, taskId, userId, roles, cancellationToken))
                    return Results.NotFound();
                if (task.FormId is null) return Results.NotFound();

                var snapshot = await workflows.GetStateAsync(task.InstanceId, cancellationToken);
                request.RecordId = snapshot.RecordId;
                var submission = await runtime.SubmitAsync(
                    task.FormId.Value, request, context.User.Identity?.Name, cancellationToken);
                if (!submission.IsValid)
                    return Results.UnprocessableEntity(new { errors = submission.Errors });

                await inbox.CompleteTaskAsync(taskId, userId, roles, request.Action, cancellationToken);
                return Results.Ok(submission.Result);
            })
            .RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Maps <see cref="DomainValidationException"/> to 422 with the error list as payload, so the
    /// client store can rehydrate the exception and forms surface per-field errors as they do on the server.
    /// </summary>
    private static async Task<IResult> GuardValidation(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (DomainValidationException dvx)
        {
            return Results.UnprocessableEntity(dvx.Errors);
        }
    }

    private static async Task<bool> CanAccessTaskAsync(
        ITaskInboxService inbox, Guid taskId, string userId, List<string> roles, CancellationToken cancellationToken) =>
        (await inbox.GetTasksForUserAsync(userId, roles, UserTaskState.Pending, cancellationToken))
            .Any(task => task.Id == taskId);

    private static FormBootstrap Localize(FormBootstrap bootstrap, IStringLocalizer<SharedResource> localizer)
    {
        bootstrap.Messages = new()
        {
            Loading = localizer["FormRuntime.Loading"],
            InvalidDefinition = localizer["FormRuntime.InvalidDefinition"],
            SelectPlaceholder = localizer["FormRuntime.SelectPlaceholder"],
            ErrorSummary = localizer["FormRuntime.ErrorSummary"],
            Submit = localizer["FormRuntime.Submit"],
            Submitting = localizer["FormRuntime.Submitting"],
            Submitted = localizer["FormRuntime.Submitted"],
            LoadFailed = localizer["FormRuntime.LoadFailed"],
            SubmissionFailed = localizer["FormRuntime.SubmissionFailed"],
            SecurityFailed = localizer["FormRuntime.SecurityFailed"],
            NotPublished = localizer["FormRuntime.NotPublished"]
        };
        return bootstrap;
    }

    private record OptionsRequest(string ValueField, string LabelField, int? DataSourceIndex, DomainQuery? Query);
}
