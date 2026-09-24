using System.Net;
using System.Net.Http.Json;
using Argent.Core.Serialization;
using Argent.Core.DomainObjects;
using Argent.Core.DomainObjects.Querying;

namespace Argent.Web.Client.Services;

public class HttpDomainObjectStore(HttpClient _http) : IDomainObjectStore
{
    public Task<DomainRecord?> GetAsync(string objectKey, Guid id) =>
        _http.GetFromJsonAsync<DomainRecord>($"/api/records/{Uri.EscapeDataString(objectKey)}/{id}", ArgentJson.Options);

    public async Task<DomainQueryResult> QueryAsync(string objectKey, DomainQuery? query = null)
    {
        var response = await _http.PostAsJsonAsync($"/api/records/{Uri.EscapeDataString(objectKey)}/query", query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DomainQueryResult>(ArgentJson.Options))!;
    }

    public async Task<DomainRecord> CreateAsync(string objectKey, IDictionary<string, object?> values, string? user = null)
    {
        var response = await _http.PostAsJsonAsync($"/api/records/{Uri.EscapeDataString(objectKey)}", values);
        await ThrowValidationErrorsAsync(response);
        return (await response.Content.ReadFromJsonAsync<DomainRecord>(ArgentJson.Options))!;
    }

    public async Task<DomainRecord> UpdateAsync(string objectKey, Guid id, IDictionary<string, object?> values, string? user = null)
    {
        var response = await _http.PutAsJsonAsync($"/api/records/{Uri.EscapeDataString(objectKey)}/{id}", values);
        await ThrowValidationErrorsAsync(response);
        return (await response.Content.ReadFromJsonAsync<DomainRecord>(ArgentJson.Options))!;
    }

    public async Task<DomainRecord> UpsertAsync(string objectKey, DomainRecord record, string? user = null)
    {
        var response = await _http.PostAsJsonAsync($"/api/records/{Uri.EscapeDataString(objectKey)}/upsert", record);
        await ThrowValidationErrorsAsync(response);
        return (await response.Content.ReadFromJsonAsync<DomainRecord>(ArgentJson.Options))!;
    }

    public async Task DeleteAsync(string objectKey, Guid id)
    {
        var response = await _http.DeleteAsync($"/api/records/{Uri.EscapeDataString(objectKey)}/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<DomainOption>> GetOptionsAsync(
        string objectKey,
        string valueField,
        string labelField,
        int? dataSourceIndex = null,
        DomainQuery? query = null)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/records/{Uri.EscapeDataString(objectKey)}/options",
            new { valueField, labelField, dataSourceIndex, query });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<DomainOption>>(ArgentJson.Options) ?? [];
    }

    public async Task<DomainQueryResult> QueryDataSourceAsync(string objectKey, int dataSourceIndex, DomainQuery? query = null)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/records/{Uri.EscapeDataString(objectKey)}/data-sources/{dataSourceIndex}/query", query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DomainQueryResult>(ArgentJson.Options))!;
    }

    /// <summary>Rehydrates the 422 payload into the same exception the EF store throws, so form error handling is identical.</summary>
    private static async Task ThrowValidationErrorsAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var errors = await response.Content.ReadFromJsonAsync<List<DomainValidationError>>(ArgentJson.Options) ?? [];
            throw new DomainValidationException(errors);
        }
        response.EnsureSuccessStatusCode();
    }
}
