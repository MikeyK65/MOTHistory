namespace MOTHistory.Maui.Models;

public class VehicleInfo
{
    public string Registration { get; set; } = "";
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public string Colour { get; set; } = "";
    public string FuelType { get; set; } = "";
    public string EngineSize { get; set; } = "";
    public DateTime? FirstUsedDate { get; set; }

    public string MakeModel => $"{Make} {Model}".Trim();

    public string ColourFuelEngine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Colour)) parts.Add(ToTitleCase(Colour));
            if (!string.IsNullOrWhiteSpace(FuelType)) parts.Add(ToTitleCase(FuelType));
            if (!string.IsNullOrWhiteSpace(EngineSize)) parts.Add($"{EngineSize}cc");
            return string.Join(" · ", parts);
        }
    }

    public string FirstUsedDisplay =>
        FirstUsedDate.HasValue ? $"First used {FirstUsedDate.Value:MMMM yyyy}" : "";

    public bool HasDetail => !string.IsNullOrWhiteSpace(MakeModel) || FirstUsedDate.HasValue;

    private static string ToTitleCase(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}
