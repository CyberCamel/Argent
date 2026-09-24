using Argent.Core.Workflows.Designer;
using System.Net.Http.Json;

namespace Argent.Web.Client.Services;

public class HttpWorkflowInstanceViewStore(HttpClient _http) : IWorkflowInstanceViewStore
{
    public Task<WorkflowInstanceViewDto?> LoadAsync(Guid instanceId) =>
        _http.GetFromJsonAsync<WorkflowInstanceViewDto>($"/api/designer/workflow-instances/{instanceId}");
}
