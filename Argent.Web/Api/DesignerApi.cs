using Argent.Core.Authorization;
using Argent.Core.DomainObjects;
using Argent.Core.Forms;
using Argent.Core.Workflows.Designer;
using Argent.Core.Workflows.Execution;
using Argent.Core.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace Argent.Web.Api;

public static class DesignerApi
{
    public static IEndpointRouteBuilder MapDesignerApi(this IEndpointRouteBuilder app)
    {
        // ── Workflow Designer ──────────────────────────────────────────────────

        app.MapGet("/api/designer/workflows/{workflowId:guid}", async (Guid workflowId, IWorkflowDesignerStore store) =>
            await store.LoadWorkflowAsync(workflowId) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapGet("/api/designer/workflow-versions/{versionId:guid}", async (Guid versionId, IWorkflowDesignerStore store) =>
            await store.LoadVersionAsync(versionId) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapPost("/api/designer/workflow-drafts", async ([FromBody] WorkflowSaveDraftRequest req, IWorkflowDesignerStore store) =>
            Results.Ok(await store.SaveDraftAsync(req)));

        app.MapPost("/api/designer/workflow-publish", async ([FromBody] WorkflowPublishRequest req, IWorkflowDesignerStore store) =>
            Results.Ok(await store.PublishVersionAsync(req)));

        app.MapPost("/api/designer/workflow-deploy/{versionId:guid}", async (Guid versionId, IWorkflowDesignerStore store, HttpContext ctx) =>
            Results.Ok(await store.DeployVersionAsync(versionId, ctx.User?.Identity?.Name)));

        app.MapPut("/api/designer/workflow-versions/{versionId:guid}/audiences",
            async (Guid versionId, [FromBody] Dictionary<Guid, RoleAudience> audiences, IWorkflowDesignerStore store) =>
            {
                await store.SaveRoleAudiencesAsync(versionId, audiences);
                return Results.NoContent();
            });

        app.MapGet("/api/designer/workflow-versions/{versionId:guid}/audiences", async (Guid versionId, IWorkflowDesignerStore store) =>
            await store.GetVersionAudiencesAsync(versionId) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapPost("/api/designer/workflow-versions/{versionId:guid}/create-draft",
            async (Guid versionId, IWorkflowDesignerStore store, HttpContext ctx) =>
                Results.Ok(await store.CreateDraftFromVersionAsync(versionId, ctx.User?.Identity?.Name)));

        app.MapDelete("/api/designer/workflow-drafts/{draftId:guid}", async (Guid draftId, Guid workflowId, IWorkflowDesignerStore store) =>
            Results.Ok(await store.DiscardDraftAsync(draftId, workflowId)));

        app.MapGet("/api/designer/workflows/{workflowId:guid}/timeline", async (Guid workflowId, IWorkflowDesignerStore store) =>
            await store.GetVersionTimelineAsync(workflowId) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapGet("/api/designer/workflows/{workflowId:guid}/diff", async (Guid workflowId, IWorkflowDesignerStore store) =>
            await store.GetDraftDiffAsync(workflowId) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapGet("/api/designer/workflows/{workflowId:guid}/start-form", async (Guid workflowId, IWorkflowTaskStore store) =>
            Results.Ok(await store.GetStartFormIdAsync(workflowId)));

        // ── Workflow Instance View ─────────────────────────────────────────────

        app.MapGet("/api/designer/workflow-instances/{instanceId:guid}", async (Guid instanceId, IWorkflowInstanceViewStore store) =>
            await store.LoadAsync(instanceId) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapGet("/api/designer/workflow-instances/{instanceId:guid}/nodes/{nodeId:guid}/actions",
            async (Guid instanceId, Guid nodeId, IWorkflowTaskStore store) =>
                Results.Ok(await store.GetTaskActionsAsync(instanceId, nodeId)));

        app.MapGet("/api/designer/workflow-instances/{instanceId:guid}/nodes/{nodeId:guid}/view-mode",
            async (Guid instanceId, Guid nodeId, IWorkflowTaskStore store) =>
                Results.Ok(await store.GetTaskViewModeAsync(instanceId, nodeId)));

        app.MapGet("/api/designer/workflow-instances/{instanceId:guid}/nodes/{nodeId:guid}/action-descriptors",
            async (Guid instanceId, Guid nodeId, IWorkflowTaskStore store) =>
                Results.Ok(await store.GetTaskActionDescriptorsAsync(instanceId, nodeId)))
            .RequireAuthorization();

        // ── Form Designer ──────────────────────────────────────────────────────

        app.MapGet("/api/designer/forms/{formDesignId:guid}", async (Guid formDesignId, IFormDesignerStore store) =>
            await store.LoadAsync(formDesignId) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapGet("/api/designer/forms/{formDesignId:guid}/view-modes",
            async (Guid formDesignId, IFormDesignerStore store) =>
                Results.Ok(await store.GetPublishedViewModesAsync(formDesignId)));

        app.MapGet("/api/designer/form-versions/{versionId:guid}", async (Guid versionId, IFormDesignerStore store) =>
            await store.LoadVersionAsync(versionId) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapPost("/api/designer/form-drafts", async ([FromBody] FormDesignerSaveRequest req, IFormDesignerStore store, HttpContext ctx) =>
        {
            var userId = ctx.User!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
            var reqWithUser = new FormDesignerSaveRequest
            {
                FormDesignId = req.FormDesignId,
                Name = req.Name,
                Description = req.Description,
                Definition = req.Definition,
                UserName = ctx.User.Identity?.Name ?? userId,
                UserIdentityId = userId
            };
            return Results.Ok(await store.SaveAsync(reqWithUser));
        });

        app.MapPost("/api/designer/form-publish", async ([FromBody] FormPublishRequest req, IFormDesignerStore store, HttpContext ctx) =>
        {
            var reqWithUser = new FormPublishRequest { FormDesignId = req.FormDesignId, UserId = ctx.User!.Identity?.Name! };
            return Results.Ok(await store.PublishAsync(reqWithUser));
        });

        app.MapPost("/api/designer/form-versions/{versionId:guid}/create-draft",
            async (Guid versionId, IFormDesignerStore store, HttpContext ctx) =>
                Results.Ok(await store.CreateDraftFromVersionAsync(versionId, ctx.User?.Identity?.Name)));

        app.MapGet("/api/designer/forms", async (string objectKey, IFormDesignerStore store) =>
            Results.Ok(await store.GetSummariesByObjectKeyAsync(objectKey)));

        app.MapGet("/api/designer/forms/field-names", async (string objectKey, IFormDesignerStore store) =>
            Results.Ok(await store.GetPublishedFieldNamesByObjectKeyAsync(objectKey)));

        app.MapGet("/api/form-data/{recordId:guid}/{formId:guid}", async (Guid recordId, Guid formId, IFormDataStore store) =>
            Results.Ok(await store.GetCustomDataAsync(recordId, formId)));

        app.MapPut("/api/form-data/{recordId:guid}/{formId:guid}",
            async (Guid recordId, Guid formId, [FromBody] Dictionary<string, object?> data, IFormDataStore store) =>
            {
                await store.SaveCustomDataAsync(recordId, formId, data);
                return Results.NoContent();
            });

        // ── Authorization / Directory ──────────────────────────────────────────

        app.MapGet("/api/authorization/subjects", async (ISubjectDirectory directory) =>
            Results.Ok(await directory.GetSubjectsAsync()));

        app.MapGet("/api/authorization/users", async (IGroupService groups) =>
            Results.Ok(await groups.GetUsersAsync()));

        app.MapGet("/api/authorization/groups", async (Guid? excludeId, IGroupService groups) =>
            Results.Ok(await groups.GetGroupsAsync(excludeId)));

        app.MapGet("/api/authorization/groups/{groupId:guid}", async (Guid groupId, IGroupService groups) =>
            await groups.GetAsync(groupId) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapPost("/api/authorization/groups", async ([FromBody] GroupCreateRequest req,
            IGroupService groups, IPolicyDecisionService policies) =>
        {
            var id = await groups.CreateAsync(req.Name, req.Description, req.UserIds, req.ChildGroupIds);
            await policies.InvalidateCacheAsync();
            return Results.Ok(id);
        });

        app.MapPut("/api/authorization/groups/{groupId:guid}",
            async (Guid groupId, [FromBody] GroupCreateRequest req, IGroupService groups,
                IPolicyDecisionService policies) =>
            {
                await groups.UpdateAsync(groupId, req.Name, req.Description, req.UserIds, req.ChildGroupIds);
                await policies.InvalidateCacheAsync();
                return Results.NoContent();
            });

        // ── Resource Ownership ────────────────────────────────────────────────

        app.MapPost("/api/authorization/ownership/grant",
            async ([FromBody] OwnershipGrantRequest req, IResourceOwnershipService svc) =>
            {
                await svc.GrantOwnershipAsync(req.ResourceType, req.ResourceId, req.UserId);
                return Results.NoContent();
            });

        app.MapPost("/api/authorization/ownership/share",
            async ([FromBody] OwnershipShareRequest req, IResourceOwnershipService svc) =>
            {
                await svc.ShareAccessAsync(req.ResourceType, req.ResourceId, req.SubjectJson, req.Actions);
                return Results.NoContent();
            });

        app.MapDelete("/api/authorization/ownership/policies/{policyId:guid}",
            async (Guid policyId, IResourceOwnershipService svc) =>
            {
                await svc.RevokeAccessAsync(policyId);
                return Results.NoContent();
            });

        app.MapPut("/api/authorization/ownership/policies/{policyId:guid}/actions",
            async (Guid policyId, [FromBody] List<string> actions, IResourceOwnershipService svc) =>
            {
                await svc.UpdateActionsAsync(policyId, actions);
                return Results.NoContent();
            });

        app.MapGet("/api/authorization/ownership/policies",
            async (string resourceType, Guid resourceId, IResourceOwnershipService svc) =>
                Results.Ok(await svc.GetResourcePoliciesAsync(resourceType, resourceId)));

        // ── Domain Object Definition ───────────────────────────────────────────

        app.MapGet("/api/domain-objects", async (IDomainObjectDefinitionService svc) =>
            Results.Ok(await svc.GetSummariesAsync()));

        app.MapGet("/api/domain-objects/{id:guid}", async (Guid id, IDomainObjectDefinitionService svc) =>
            await svc.GetAsync(id) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapPost("/api/domain-objects", async ([FromBody] DomainObjectCreateRequest req, IDomainObjectDefinitionService svc, HttpContext ctx) =>
            Results.Ok(await svc.CreateAsync(req.Key, req.Name, req.Description, ctx.User?.Identity?.Name)));

        app.MapGet("/api/domain-objects/{id:guid}/working-definition", async (Guid id, IDomainObjectDefinitionService svc) =>
            await svc.GetWorkingDefinitionAsync(id) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapGet("/api/domain-objects/by-key/{key}/published", async (string key, IDomainObjectDefinitionService svc) =>
            await svc.GetPublishedDefinitionAsync(key) is { } r ? Results.Ok(r) : Results.NotFound());

        app.MapPut("/api/domain-objects/{id:guid}/draft",
            async (Guid id, [FromBody] DomainObjectDraftSaveRequest req, IDomainObjectDefinitionService svc, HttpContext ctx) =>
            {
                await svc.SaveDraftAsync(id, req.Definition, ctx.User?.Identity?.Name);
                return Results.NoContent();
            });

        app.MapPost("/api/domain-objects/{id:guid}/publish", async (Guid id, IDomainObjectDefinitionService svc, HttpContext ctx) =>
            Results.Ok(await svc.PublishAsync(id, ctx.User?.Identity?.Name)));

        app.MapGet("/api/domain-objects/{id:guid}/versions", async (Guid id, IDomainObjectDefinitionService svc) =>
            Results.Ok(await svc.GetVersionsAsync(id)));

        app.MapGet("/api/domain-objects/versions/{versionId:guid}", async (Guid versionId, IDomainObjectDefinitionService svc) =>
            await svc.GetVersionAsync(versionId) is { } r ? Results.Ok(r) : Results.NotFound());

        return app;
    }

    private record GroupCreateRequest(string Name, string? Description, List<Guid> UserIds, List<Guid> ChildGroupIds);
    private record OwnershipGrantRequest(string ResourceType, Guid ResourceId, string UserId);
    private record OwnershipShareRequest(string ResourceType, Guid ResourceId, string SubjectJson, List<string> Actions);
    private record DomainObjectCreateRequest(string Key, string Name, string? Description);
    private record DomainObjectDraftSaveRequest(DomainObjectDefinition Definition);
}
