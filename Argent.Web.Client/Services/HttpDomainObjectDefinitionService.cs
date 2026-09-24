using Argent.Core.DomainObjects;
using System.Net.Http.Json;

namespace Argent.Web.Client.Services;

public class HttpDomainObjectDefinitionService(HttpClient _http) : IDomainObjectDefinitionService
{
    public async Task<List<DomainObjectSummary>> GetSummariesAsync()
    {
        var result = await _http.GetFromJsonAsync<List<DomainObjectSummary>>("/api/domain-objects");
        return result ?? [];
    }

    public Task<DomainObject?> GetAsync(Guid id) =>
        _http.GetFromJsonAsync<DomainObject>($"/api/domain-objects/{id}");

    public async Task<DomainObject> CreateAsync(string key, string name, string? description = null, string? createdBy = null)
    {
        var response = await _http.PostAsJsonAsync("/api/domain-objects", new { key, name, description });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DomainObject>())!;
    }

    public Task<DomainObjectDefinition?> GetWorkingDefinitionAsync(Guid id) =>
        _http.GetFromJsonAsync<DomainObjectDefinition>($"/api/domain-objects/{id}/working-definition");

    public Task<DomainObjectDefinition?> GetPublishedDefinitionAsync(string key) =>
        _http.GetFromJsonAsync<DomainObjectDefinition>($"/api/domain-objects/by-key/{Uri.EscapeDataString(key)}/published");

    public async Task SaveDraftAsync(Guid id, DomainObjectDefinition definition, string? updatedBy = null)
    {
        var response = await _http.PutAsJsonAsync($"/api/domain-objects/{id}/draft", new { definition, updatedBy });
        response.EnsureSuccessStatusCode();
    }

    public async Task<DomainObjectVersion> PublishAsync(Guid id, string? createdBy = null)
    {
        var response = await _http.PostAsJsonAsync($"/api/domain-objects/{id}/publish", new { createdBy });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DomainObjectVersion>())!;
    }

    public async Task<List<DomainObjectVersion>> GetVersionsAsync(Guid id)
    {
        var result = await _http.GetFromJsonAsync<List<DomainObjectVersion>>($"/api/domain-objects/{id}/versions");
        return result ?? [];
    }

    public Task<DomainObjectVersion?> GetVersionAsync(Guid versionId) =>
        _http.GetFromJsonAsync<DomainObjectVersion>($"/api/domain-objects/versions/{versionId}");
}
