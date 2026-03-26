using Elsa.Studio.Contracts;
using Elsa.Studio.Login.Contracts;
using Elsa.Studio.Login.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Argent.Studio;

/// <summary>
/// Validates credentials against your custom token endpoint.
/// </summary>
public class CustomTokenCredentialsValidator : ICredentialsValidator
{
    private readonly HttpClient _client;

    public CustomTokenCredentialsValidator(HttpClient client)
    {
        _client = client;
    }

    public async ValueTask<ValidateCredentialsResult> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {

            var request = new { username, password };
            var response = await _client.PostAsJsonAsync(
                "api/token",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new(false, null, null);

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(
                cancellationToken: cancellationToken);

            if (tokenResponse?.AccessToken == null)
                return new(false, null, null);

            return new(true, tokenResponse.AccessToken, "null");
        }
        catch
        {
            return new(false, null, null);
        }
    }
}

/// <summary>
/// Matches your TokenController response format.
/// </summary>
public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}