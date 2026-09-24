using Argent.Core.Workflows.Designer;
using Argent.Core.Workflows;
using System.Net.Http.Json;

namespace Argent.Web.Client.Services;

public class HttpWorkflowDesignerStore(HttpClient _http) : IWorkflowDesignerStore
{
    public Task<WorkflowDesignerLoadResult?> LoadWorkflowAsync(Guid workflowId) =>
        _http.GetFromJsonAsync<WorkflowDesignerLoadResult>($"/api/designer/workflows/{workflowId}");

    public Task<WorkflowDesignerLoadResult?> LoadVersionAsync(Guid versionId) =>
        _http.GetFromJsonAsync<WorkflowDesignerLoadResult>($"/api/designer/workflow-versions/{versionId}");

    public async Task<WorkflowSaveDraftResult> SaveDraftAsync(WorkflowSaveDraftRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/designer/workflow-drafts", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkflowSaveDraftResult>())!;
    }

    public async Task<WorkflowPublishResult> PublishVersionAsync(WorkflowPublishRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/designer/workflow-publish", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkflowPublishResult>())!;
    }

    public async Task<WorkflowDesignerLoadResult> DeployVersionAsync(Guid versionId, string? userId = null)
    {
        var response = await _http.PostAsJsonAsync($"/api/designer/workflow-deploy/{versionId}", (object?)null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkflowDesignerLoadResult>())!;
    }

    public async Task SaveRoleAudiencesAsync(Guid versionId, Dictionary<Guid, RoleAudience> audiences)
    {
        var response = await _http.PutAsJsonAsync($"/api/designer/workflow-versions/{versionId}/audiences", audiences);
        response.EnsureSuccessStatusCode();
    }

    public Task<Dictionary<Guid, RoleAudience>?> GetVersionAudiencesAsync(Guid versionId) =>
        _http.GetFromJsonAsync<Dictionary<Guid, RoleAudience>>($"/api/designer/workflow-versions/{versionId}/audiences");

    public async Task<WorkflowSaveDraftResult> CreateDraftFromVersionAsync(Guid versionId, string? userId = null)
    {
        var response = await _http.PostAsJsonAsync($"/api/designer/workflow-versions/{versionId}/create-draft", (object?)null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkflowSaveDraftResult>())!;
    }

    public async Task<WorkflowDesignerLoadResult?> DiscardDraftAsync(Guid draftId, Guid workflowId)
    {
        var response = await _http.DeleteAsync($"/api/designer/workflow-drafts/{draftId}?workflowId={workflowId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkflowDesignerLoadResult>();
    }

    public Task<WorkflowVersionTimeline?> GetVersionTimelineAsync(Guid workflowId) =>
        _http.GetFromJsonAsync<WorkflowVersionTimeline>($"/api/designer/workflows/{workflowId}/timeline");

    public Task<WorkflowDiff?> GetDraftDiffAsync(Guid workflowId) =>
        _http.GetFromJsonAsync<WorkflowDiff>($"/api/designer/workflows/{workflowId}/diff");
}
