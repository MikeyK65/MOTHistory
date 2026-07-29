using System.Text.Json;

namespace MOTHistory.Shared;

/// <summary>
/// The subset of the DVLA Vehicle Enquiry Service (VES) response the app displays.
/// VES is the only source in this app for tax status and CO2/Euro emissions data —
/// the DVSA MOT History API returns none of it.
/// </summary>
public class VehicleTaxInfo
{
    public string Registration { get; set; } = "";

    // Tax
    public string? TaxStatus { get; set; }
    public DateTime? TaxDueDate { get; set; }

    // MOT (DVLA's own view — we still prefer DVSA test history for detail)
    public string? MotStatus { get; set; }
    public DateTime? MotExpiryDate { get; set; }

    // Vehicle identity / spec
    public string? Make { get; set; }
    public string? Colour { get; set; }
    public string? FuelType { get; set; }
    public int? YearOfManufacture { get; set; }
    public int? EngineCapacity { get; set; }
    public int? Co2Emissions { get; set; }
    public string? EuroStatus { get; set; }
    public string? RealDrivingEmissions { get; set; }
    public string? TypeApproval { get; set; }
    public string? Wheelplan { get; set; }
    public int? RevenueWeight { get; set; }

    // Provenance markers
    public bool MarkedForExport { get; set; }
    public DateTime? MonthOfFirstRegistration { get; set; }
    public DateTime? DateOfLastV5CIssued { get; set; }

    public bool IsTaxed =>
        string.Equals(TaxStatus, "Taxed", StringComparison.OrdinalIgnoreCase);

    public bool IsSorn =>
        TaxStatus is not null &&
        TaxStatus.Contains("SORN", StringComparison.OrdinalIgnoreCase);

    public bool IsUntaxed =>
        TaxStatus is not null && !IsTaxed && !IsSorn;

    public string TaxStatusDisplay => string.IsNullOrWhiteSpace(TaxStatus) ? "Unknown" : TaxStatus!;

    public int? DaysUntilTaxDue =>
        TaxDueDate.HasValue ? (int)(TaxDueDate.Value.Date - DateTime.Today).TotalDays : null;

    public bool IsTaxDueSoon => DaysUntilTaxDue is >= 0 and <= 30;

    public bool IsTaxOverdue => DaysUntilTaxDue is < 0;

    public string TaxDueDisplay =>
        TaxDueDate.HasValue ? $"Tax due {TaxDueDate.Value:d MMM yyyy}" : "";

    public string Co2Display =>
        Co2Emissions.HasValue && Co2Emissions.Value > 0 ? $"{Co2Emissions.Value} g/km" : "";

    public string EuroStatusDisplay =>
        string.IsNullOrWhiteSpace(EuroStatus) ? "" : $"Euro {EuroStatus!.Replace("EURO", "", StringComparison.OrdinalIgnoreCase).Trim()}";

    /// <summary>
    /// A V5C (logbook) issued in the last 90 days often means a recent keeper change —
    /// worth flagging to someone viewing a car to buy.
    /// </summary>
    public bool V5CRecentlyIssued =>
        DateOfLastV5CIssued.HasValue &&
        (DateTime.Today - DateOfLastV5CIssued.Value.Date).TotalDays is >= 0 and <= 90;

    public bool HasAnyDetail =>
        !string.IsNullOrWhiteSpace(TaxStatus) || TaxDueDate.HasValue || Co2Emissions.HasValue;

    /// <summary>Parses a VES vehicle response. Returns null if the payload isn't an object.</summary>
    public static VehicleTaxInfo? Parse(JsonElement json, string registration)
    {
        if (json.ValueKind != JsonValueKind.Object) return null;

        string? Str(string key) =>
            json.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() : null;

        int? Num(string key) =>
            json.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)
                ? n : null;

        bool Flag(string key) =>
            json.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.True;

        DateTime? Date(string key)
        {
            var raw = Str(key);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            // VES uses "yyyy-MM-dd" for dates and "yyyy-MM" for monthOfFirstRegistration.
            if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                return dt;
            if (DateTime.TryParseExact(raw + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var month))
                return month;
            return null;
        }

        return new VehicleTaxInfo
        {
            Registration = registration,
            TaxStatus = Str("taxStatus"),
            TaxDueDate = Date("taxDueDate"),
            MotStatus = Str("motStatus"),
            MotExpiryDate = Date("motExpiryDate"),
            Make = Str("make"),
            Colour = Str("colour"),
            FuelType = Str("fuelType"),
            YearOfManufacture = Num("yearOfManufacture"),
            EngineCapacity = Num("engineCapacity"),
            Co2Emissions = Num("co2Emissions"),
            EuroStatus = Str("euroStatus"),
            RealDrivingEmissions = Str("realDrivingEmissions"),
            TypeApproval = Str("typeApproval"),
            Wheelplan = Str("wheelplan"),
            RevenueWeight = Num("revenueWeight"),
            MarkedForExport = Flag("markedForExport"),
            MonthOfFirstRegistration = Date("monthOfFirstRegistration"),
            DateOfLastV5CIssued = Date("dateOfLastV5CIssued")
        };
    }
}
