namespace MOTHistory.Maui.Services;

public class OfflineCacheService
{
    /// <summary>Cache slot for the DVSA MOT History payload.</summary>
    public const string MotKind = "mot";

    /// <summary>Cache slot for the DVLA Vehicle Enquiry Service payload.</summary>
    public const string DvlaKind = "dvla";

    private static string CachePath(string registration, string kind) =>
        Path.Combine(FileSystem.CacheDirectory, $"{kind}_{registration.ToUpperInvariant()}.json");

    public void Save(string registration, string json, string kind = MotKind)
    {
        try { File.WriteAllText(CachePath(registration, kind), json); }
        catch { }
    }

    public string? Load(string registration, string kind = MotKind)
    {
        try
        {
            var path = CachePath(registration, kind);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch { return null; }
    }

    public DateTime? CachedAt(string registration, string kind = MotKind)
    {
        try
        {
            var path = CachePath(registration, kind);
            return File.Exists(path) ? File.GetLastWriteTime(path) : null;
        }
        catch { return null; }
    }

    public bool Has(string registration, string kind = MotKind) =>
        File.Exists(CachePath(registration, kind));
}
