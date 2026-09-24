using Argent.Core.Authorization;
using System.Net.Http.Json;

namespace Argent.Web.Client.Services;

public class HttpSubjectDirectory(HttpClient _http) : ISubjectDirectory
{
    public async Task<IReadOnlyList<SubjectEntry>> GetSubjectsAsync()
    {
        var result = await _http.GetFromJsonAsync<List<SubjectEntry>>("/api/authorization/subjects");
        return result ?? [];
    }
}
