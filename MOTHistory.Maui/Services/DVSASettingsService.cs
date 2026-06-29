using MOTHistory.Maui.Models;

namespace MOTHistory.Maui.Services;

/// <summary>
/// Stores and retrieves DVSA API credentials using Preferences (NSUserDefaults on iOS).
/// All fields use Preferences — no Keychain access required, which avoids entitlement
/// issues on developer provisioning profiles.
/// </summary>
public class DVSASettingsService
{
    private const string KeyApiKey = "dvsa_api_key";
    private const string KeyClientId = "dvsa_client_id";
    private const string KeyClientSecret = "dvsa_client_secret";
    private const string KeyBaseUrl = "dvsa_base_url";
    private const string KeyScopeUrl = "dvsa_scope_url";
    private const string KeyTokenUrl = "dvsa_token_url";

    public Task<DVSASettings> GetAsync()
    {
        var settings = new DVSASettings
        {
            ApiKey = Preferences.Default.Get(KeyApiKey, ""),
            ClientId = Preferences.Default.Get(KeyClientId, ""),
            ClientSecret = Preferences.Default.Get(KeyClientSecret, ""),
            BaseUrl = Preferences.Default.Get(KeyBaseUrl, ""),
            ScopeUrl = Preferences.Default.Get(KeyScopeUrl, ""),
            TokenUrl = Preferences.Default.Get(KeyTokenUrl, ""),
        };
        return Task.FromResult(settings);
    }

    public Task SaveAsync(DVSASettings settings)
    {
        Preferences.Default.Set(KeyApiKey, settings.ApiKey ?? "");
        Preferences.Default.Set(KeyClientId, settings.ClientId ?? "");
        Preferences.Default.Set(KeyClientSecret, settings.ClientSecret ?? "");
        Preferences.Default.Set(KeyBaseUrl, settings.BaseUrl ?? "");
        Preferences.Default.Set(KeyScopeUrl, settings.ScopeUrl ?? "");
        Preferences.Default.Set(KeyTokenUrl, settings.TokenUrl ?? "");
        return Task.CompletedTask;
    }
}
