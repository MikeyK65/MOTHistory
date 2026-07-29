namespace MOTHistory.Maui.Models;

public class DVSASettings
{
    // DVSA MOT History API (OAuth2 client credentials + x-api-key)
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? ScopeUrl { get; set; }
    public string? TokenUrl { get; set; }

    // DVLA Vehicle Enquiry Service (x-api-key only — no OAuth)
    public string? DvlaApiKey { get; set; }
    public string? DvlaBaseUrl { get; set; }

    public bool HasDvlaCredentials => !string.IsNullOrWhiteSpace(DvlaApiKey);
}
