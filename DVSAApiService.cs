using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class DVSAApiService
{
    private readonly DVSASettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public DVSAApiService(DVSASettingsService settingsService, HttpClient httpClient)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
    }

    private async Task<string?> GetAccessTokenAsync(string clientId, string clientSecret, string scope, string tokenUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            var pairs = new List<KeyValuePair<string, string>>
            {
                new("client_id", clientId),
                new("client_secret", clientSecret),
                new("scope", scope),
                new("grant_type", "client_credentials")
            };

            req.Content = new FormUrlEncodedContent(pairs);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.UserAgent.ParseAdd("dvsa-token-client/1.0");

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            System.Diagnostics.Debug.WriteLine($"Token endpoint response: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");

            if (!resp.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine("Failed to acquire token from token endpoint.");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("access_token", out var at))
            {
                return at.GetString();
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetAccessTokenAsync exception: {ex}");
            return null;
        }
    }

    public async Task<(JsonElement? Json, string? Error)> GetMotHistoryAsync(string registration, CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Current;
        var baseUrl = settings.BaseUrl?.TrimEnd('/') ?? "https://history.mot.api.gov.uk";
        var apiKey = settings.ApiKey;
        var clientId = settings.ClientId;
        var clientSecret = settings.ClientSecret;
        var scopeUrl = settings.ScopeUrl;
        var tokenUrl = settings.TokenUrl;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (null, "Missing API key. Enter your DVSA API credentials on the Settings page.");
        }

        // The DVSA MOT History API requires an OAuth access token on every request,
        // regardless of access tier.
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(scopeUrl) || string.IsNullOrWhiteSpace(tokenUrl))
        {
            return (null, "Missing Client ID/Secret/Scope URL/Token URL. Enter your DVSA API settings on the Settings page.");
        }

        // Ensure registration is uppercase, trimmed and internal whitespace removed
        var reg = new string(registration.Trim().ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        var url = $"{baseUrl}/v1/trade/vehicles/registration/{Uri.EscapeDataString(reg)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var accessToken = await GetAccessTokenAsync(clientId!, clientSecret!, scopeUrl!, tokenUrl!, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            var tokenFailMsg = "Failed to acquire access token from token endpoint. Check your Client ID/Secret/Token URL/Scope URL and network. See debug logs for token endpoint response.";
            return (null, tokenFailMsg);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey!.Trim());

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("dvsa-client/1.0");

        // Debug: log request details (don't log full secrets)
        System.Diagnostics.Debug.WriteLine($"MOT API Request: {url}");
        var masked = apiKey != null && apiKey.Length > 8 ? apiKey.Substring(0, 8) + "..." : "[REDACTED]";
        System.Diagnostics.Debug.WriteLine("Headers: Authorization=Bearer [REDACTED]");
        System.Diagnostics.Debug.WriteLine($"Headers: x-api-key={masked}");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        // Debug: log response details
        System.Diagnostics.Debug.WriteLine($"MOT API Response: {(int)response.StatusCode} {response.StatusCode}\n{payload}");

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // return a clearer error for auth failures
                var authMsg = $"MOT history API auth error {(int)response.StatusCode} {response.StatusCode}: {payload}";
                return (null, authMsg);
            }
            var msg = $"MOT history API error {(int)response.StatusCode} {response.StatusCode}: {payload}";
            return (null, msg);
        }

        using var doc = JsonDocument.Parse(payload);
        // Clone so the returned element stays valid after doc is disposed at the end of this method.
        return (doc.RootElement.Clone(), null);
    }
}
