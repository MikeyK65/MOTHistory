using System.Net.Http.Headers;
using System.Text.Json;
using MOTHistory.Maui.Models;

namespace MOTHistory.Maui.Services;

public class DVSAApiService
{
    private readonly DVSASettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public DVSAApiService(DVSASettingsService settingsService, HttpClient httpClient)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
    }

    private async Task<string?> GetAccessTokenAsync(string clientId, string clientSecret, string scope, string tokenUrl, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            req.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("scope", scope),
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
            });
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.UserAgent.ParseAdd("dvsa-token-client/1.0");

            using var resp = await _httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(JsonElement? Json, string? Error)> GetMotHistoryAsync(string registration, CancellationToken ct = default)
    {
        var settings = await _settingsService.GetAsync();

        var baseUrl = settings.BaseUrl?.TrimEnd('/');
        var apiKey = settings.ApiKey;
        var clientId = settings.ClientId;
        var clientSecret = settings.ClientSecret;
        var scopeUrl = settings.ScopeUrl;
        var tokenUrl = settings.TokenUrl;

        if (string.IsNullOrWhiteSpace(apiKey))
            return (null, "Missing API key. Please enter your DVSA credentials on the Settings tab.");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)
            || string.IsNullOrWhiteSpace(scopeUrl) || string.IsNullOrWhiteSpace(tokenUrl))
            return (null, "Missing OAuth credentials. Please complete the Settings tab.");

        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "https://history.mot.api.gov.uk";

        var reg = new string(registration.Trim().ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (string.IsNullOrEmpty(reg))
            return (null, "Please enter a registration number.");

        var url = $"{baseUrl}/v1/trade/vehicles/registration/{Uri.EscapeDataString(reg)}";

        var accessToken = await GetAccessTokenAsync(clientId!, clientSecret!, scopeUrl!, tokenUrl!, ct);
        if (string.IsNullOrWhiteSpace(accessToken))
            return (null, "Failed to acquire access token. Check your Client ID, Secret, Token URL, and Scope URL in Settings.");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey!.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("dvsa-client/1.0");

        using var response = await _httpClient.SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? (null, $"Authentication error ({(int)response.StatusCode}). Check your API key and OAuth credentials in Settings.")
                : (null, $"API error {(int)response.StatusCode}: {payload}");
        }

        using var doc = JsonDocument.Parse(payload);
        return (doc.RootElement.Clone(), null);
    }
}
