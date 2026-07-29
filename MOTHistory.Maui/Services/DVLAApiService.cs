using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MOTHistory.Maui.Services;

/// <summary>
/// Calls the DVLA Vehicle Enquiry Service (VES) API, which supplies tax status, tax due
/// date, CO2/Euro emissions data and export markers — none of which the DVSA MOT History
/// API returns. Unlike DVSA, VES needs only an <c>x-api-key</c> header (no OAuth token).
/// </summary>
public class DVLAApiService
{
    public const string DefaultBaseUrl = "https://driver-vehicle-licensing.api.gov.uk";

    private readonly DVSASettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public DVLAApiService(DVSASettingsService settingsService, HttpClient httpClient)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Looks up a vehicle in VES. Returns <c>(null, null)</c> when no DVLA key is
    /// configured — the caller treats that as "tax data unavailable" rather than an
    /// error, so the app still works with MOT data alone.
    /// </summary>
    public async Task<(JsonElement? Json, string? Error)> GetVehicleAsync(string registration, CancellationToken ct = default)
    {
        var settings = await _settingsService.GetAsync();

        var apiKey = settings.DvlaApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return (null, null);

        var baseUrl = settings.DvlaBaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = DefaultBaseUrl;

        var reg = new string(registration.Trim().ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (string.IsNullOrEmpty(reg))
            return (null, "Please enter a registration number.");

        var url = $"{baseUrl}/vehicle-enquiry/v1/vehicles";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey!.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("dvla-ves-client/1.0");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { registrationNumber = reg }),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // 404 just means DVLA holds no record — not a configuration problem.
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return (null, null);

                return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? (null, "DVLA authentication error. Check your DVLA API key in Settings.")
                    : (null, $"DVLA API error {(int)response.StatusCode}: {ExtractError(payload)}");
            }

            using var doc = JsonDocument.Parse(payload);
            // Clone so the element stays valid after doc is disposed.
            return (doc.RootElement.Clone(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, $"DVLA lookup failed: {ex.Message}");
        }
    }

    /// <summary>Pulls the human-readable message out of a VES error envelope, if present.</summary>
    private static string ExtractError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                if (first.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                    return detail.GetString() ?? payload;
                if (first.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    return title.GetString() ?? payload;
            }
        }
        catch
        {
            // fall through to raw payload
        }

        return payload;
    }
}
