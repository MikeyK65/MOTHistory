using System.Text.Json;

namespace MOTHistory.Maui.Services;

public class RecentSearchesService
{
    private const string Key = "recent_searches";
    private const int MaxCount = 10;

    public List<string> Get()
    {
        var json = Preferences.Default.Get(Key, "[]");
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    public void Add(string registration)
    {
        var list = Get();
        list.RemoveAll(r => r.Equals(registration, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, registration);
        if (list.Count > MaxCount) list.RemoveRange(MaxCount, list.Count - MaxCount);
        Preferences.Default.Set(Key, JsonSerializer.Serialize(list));
    }

    public void Clear()
    {
        Preferences.Default.Set(Key, "[]");
    }
}
