using Argent.Core.Forms;
using System.Net.Http.Json;

namespace Argent.Web.Client.Services;

public class HttpFormDesignerStore(HttpClient _http) : IFormDesignerStore
{
    public Task<FormDesignerLoadResult?> LoadAsync(Guid formDesignId) =>
        _http.GetFromJsonAsync<FormDesignerLoadResult>($"/api/designer/forms/{formDesignId}");

    public Task<FormDesignerLoadResult?> LoadVersionAsync(Guid versionId) =>
        _http.GetFromJsonAsync<FormDesignerLoadResult>($"/api/designer/form-versions/{versionId}");

    public async Task<FormDesignerSaveResult> SaveAsync(FormDesignerSaveRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/designer/form-drafts", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FormDesignerSaveResult>())!;
    }

    public async Task<FormDesignVersion> PublishAsync(FormPublishRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/designer/form-publish", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FormDesignVersion>())!;
    }

    public async Task<FormDesignerLoadResult?> CreateDraftFromVersionAsync(Guid versionId, string? userName = null)
    {
        var response = await _http.PostAsJsonAsync($"/api/designer/form-versions/{versionId}/create-draft", (object?)null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FormDesignerLoadResult>();
    }

    public async Task<IReadOnlyList<FormDesignSummary>> GetSummariesByObjectKeyAsync(string objectKey)
    {
        var result = await _http.GetFromJsonAsync<List<FormDesignSummary>>($"/api/designer/forms?objectKey={Uri.EscapeDataString(objectKey)}");
        return result ?? [];
    }

    public async Task<IReadOnlyList<string>> GetPublishedFieldNamesByObjectKeyAsync(string objectKey)
    {
        var result = await _http.GetFromJsonAsync<List<string>>($"/api/designer/forms/field-names?objectKey={Uri.EscapeDataString(objectKey)}");
        return result ?? [];
    }
}
