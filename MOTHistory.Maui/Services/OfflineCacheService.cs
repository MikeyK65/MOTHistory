namespace MOTHistory.Maui.Services;

public class OfflineCacheService
{
    private static string CachePath(string registration) =>
        Path.Combine(FileSystem.CacheDirectory, $"mot_{registration.ToUpperInvariant()}.json");

    public void Save(string registration, string json)
    {
        try { File.WriteAllText(CachePath(registration), json); }
        catch { }
    }

    public string? Load(string registration)
    {
        try
        {
            var path = CachePath(registration);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch { return null; }
    }

    public DateTime? CachedAt(string registration)
    {
        try
        {
            var path = CachePath(registration);
            return File.Exists(path) ? File.GetLastWriteTime(path) : null;
        }
        catch { return null; }
    }

    public bool Has(string registration) => File.Exists(CachePath(registration));
}
