using System.Text.Json;
using MOTHistory.Maui.Models;

namespace MOTHistory.Maui.Services;

public class SavedVehiclesService
{
    private const string Key = "saved_vehicles";

    public List<SavedVehicle> GetAll()
    {
        var json = Preferences.Default.Get(Key, "[]");
        try { return JsonSerializer.Deserialize<List<SavedVehicle>>(json) ?? []; }
        catch { return []; }
    }

    public bool IsSaved(string registration) =>
        GetAll().Any(v => v.Registration.Equals(registration, StringComparison.OrdinalIgnoreCase));

    public void Save(SavedVehicle vehicle)
    {
        var list = GetAll();
        list.RemoveAll(v => v.Registration.Equals(vehicle.Registration, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, vehicle);
        Persist(list);
    }

    public void Remove(string registration)
    {
        var list = GetAll();
        list.RemoveAll(v => v.Registration.Equals(registration, StringComparison.OrdinalIgnoreCase));
        Persist(list);
    }

    private static void Persist(List<SavedVehicle> list) =>
        Preferences.Default.Set(Key, JsonSerializer.Serialize(list));
}
