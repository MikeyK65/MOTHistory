using System.Text.Json;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Holds the active DVSA API credentials/settings for the app. Values entered on the
/// Settings page are persisted to a JSON file outside the repo (LocalApplicationData),
/// so secrets entered at runtime never end up in source control. On first run, with no
/// persisted file yet, falls back to the DVSAApi section in appsettings.json.
/// </summary>
public class DVSASettingsService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private DVSASettings _settings;

    public DVSASettingsService(IConfiguration config)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MOTHistory");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "dvsa-settings.json");

        _settings = LoadFromDisk() ?? LoadFromConfig(config);
    }

    public DVSASettings Current
    {
        get { lock (_lock) { return Clone(_settings); } }
    }

    public void Save(DVSASettings settings)
    {
        var copy = Clone(settings);
        lock (_lock)
        {
            _settings = copy;
            File.WriteAllText(_filePath, JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private DVSASettings? LoadFromDisk()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<DVSASettings>(json);
        }
        catch
        {
            return null;
        }
    }

    private static DVSASettings LoadFromConfig(IConfiguration config)
    {
        return new DVSASettings
        {
            ApiKey = config["DVSAApi:ApiKey"],
            BaseUrl = config["DVSAApi:BaseUrl"],
            ClientId = config["DVSAApi:ClientId"],
            ClientSecret = config["DVSAApi:ClientSecret"],
            ScopeUrl = config["DVSAApi:ScopeUrl"],
            TokenUrl = config["DVSAApi:TokenUrl"]
        };
    }

    private static DVSASettings Clone(DVSASettings s) => new()
    {
        ApiKey = s.ApiKey,
        BaseUrl = s.BaseUrl,
        ClientId = s.ClientId,
        ClientSecret = s.ClientSecret,
        ScopeUrl = s.ScopeUrl,
        TokenUrl = s.TokenUrl
    };
}
