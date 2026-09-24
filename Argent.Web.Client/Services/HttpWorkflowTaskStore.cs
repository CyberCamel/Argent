using Argent.Core.Workflows.Execution;
using Argent.Core.Workflows;
using System.Net.Http.Json;

namespace Argent.Web.Client.Services;

public class HttpWorkflowTaskStore(HttpClient _http) : IWorkflowTaskStore
{
    public Task<Guid?> GetStartFormIdAsync(Guid workflowId) =>
        _http.GetFromJsonAsync<Guid?>($"/api/designer/workflows/{workflowId}/start-form");

    public async Task<IReadOnlyList<string>> GetTaskActionsAsync(Guid instanceId, Guid nodeId)
    {
        var result = await _http.GetFromJsonAsync<List<string>>($"/api/designer/workflow-instances/{instanceId}/nodes/{nodeId}/actions");
        return result ?? [];
    }

    public async Task<IReadOnlyList<TaskActionDescriptor>> GetTaskActionDescriptorsAsync(Guid instanceId, Guid nodeId) =>
        await _http.GetFromJsonAsync<List<TaskActionDescriptor>>(
            $"/api/designer/workflow-instances/{instanceId}/nodes/{nodeId}/action-descriptors") ?? [];
}
