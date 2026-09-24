using Argent.Core.Authorization;
using System.Net.Http.Json;

namespace Argent.Web.Client.Services;

public class HttpResourceOwnershipService(HttpClient _http) : IResourceOwnershipService
{
    public async Task GrantOwnershipAsync(string resourceType, Guid resourceId, string userId, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/authorization/ownership/grant",
            new { resourceType, resourceId, userId }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ShareAccessAsync(string resourceType, Guid resourceId, string subjectJson, List<string> actions, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/authorization/ownership/share",
            new { resourceType, resourceId, subjectJson, actions }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RevokeAccessAsync(Guid policyId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/authorization/ownership/policies/{policyId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateActionsAsync(Guid policyId, List<string> newActions, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/authorization/ownership/policies/{policyId}/actions", newActions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<PolicyDocument>> GetResourcePoliciesAsync(string resourceType, Guid resourceId, CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<PolicyDocument>>(
            $"/api/authorization/ownership/policies?resourceType={Uri.EscapeDataString(resourceType)}&resourceId={resourceId}", ct);
        return result ?? [];
    }
}
