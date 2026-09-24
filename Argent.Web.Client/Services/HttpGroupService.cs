using Argent.Core.Authorization;
using System.Net.Http.Json;

namespace Argent.Web.Client.Services;

public class HttpGroupService(HttpClient _http) : IGroupService
{
    public async Task<IReadOnlyList<GroupMemberOption>> GetUsersAsync()
    {
        var result = await _http.GetFromJsonAsync<List<GroupMemberOption>>("/api/authorization/users");
        return result ?? [];
    }

    public async Task<IReadOnlyList<GroupMemberOption>> GetGroupsAsync(Guid? excludeId = null)
    {
        var url = excludeId.HasValue ? $"/api/authorization/groups?excludeId={excludeId}" : "/api/authorization/groups";
        var result = await _http.GetFromJsonAsync<List<GroupMemberOption>>(url);
        return result ?? [];
    }

    public Task<GroupDetail?> GetAsync(Guid groupId) =>
        _http.GetFromJsonAsync<GroupDetail>($"/api/authorization/groups/{groupId}");

    public async Task<Guid> CreateAsync(string name, string? description, IEnumerable<Guid> userIds, IEnumerable<Guid> childGroupIds)
    {
        var response = await _http.PostAsJsonAsync("/api/authorization/groups", new
        {
            name, description,
            userIds = userIds.ToList(),
            childGroupIds = childGroupIds.ToList()
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    public async Task UpdateAsync(Guid groupId, string name, string? description, IEnumerable<Guid> userIds, IEnumerable<Guid> childGroupIds)
    {
        var response = await _http.PutAsJsonAsync($"/api/authorization/groups/{groupId}", new
        {
            name, description,
            userIds = userIds.ToList(),
            childGroupIds = childGroupIds.ToList()
        });
        response.EnsureSuccessStatusCode();
    }
}
