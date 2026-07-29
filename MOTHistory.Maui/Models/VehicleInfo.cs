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

    /// <summary>
    /// DVSA's manufacturer-recall marker. The MOT History API reports this as a string —
    /// "Yes", "No", "Unknown" or "Unclassified" — so it is kept verbatim here.
    /// </summary>
    public string OutstandingRecall { get; set; } = "";

    public bool HasOutstandingRecall =>
        string.Equals(OutstandingRecall, "Yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when DVSA explicitly confirms there is no outstanding recall.</summary>
    public bool RecallChecked =>
        string.Equals(OutstandingRecall, "Yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(OutstandingRecall, "No", StringComparison.OrdinalIgnoreCase);

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
