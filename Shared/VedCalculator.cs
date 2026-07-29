namespace MOTHistory.Shared;

public record VedEstimate(
    string? Band,
    decimal? AnnualRate,
    string Basis,
    string? Caveat)
{
    public bool HasRate => AnnualRate.HasValue;

    public string RateDisplay => AnnualRate.HasValue ? $"£{AnnualRate.Value:0}" : "Unknown";
}

/// <summary>
/// Estimates annual VED ("road tax") from the DVLA-supplied CO2 figure, fuel type,
/// engine size and first-registration date.
///
/// IMPORTANT: the rates below are for the tax year named in <see cref="RateTableTaxYear"/>
/// and HMRC/DVLA revise them every April. Update <see cref="RateTableTaxYear"/> and the
/// tables in this file each April, and always present the output as an estimate — the
/// £40,000 list-price supplement in particular cannot be determined from VES data.
/// Authoritative rates: https://www.gov.uk/vehicle-tax-rate-tables
/// </summary>
public static class VedCalculator
{
    public const string RateTableTaxYear = "2025/26";

    private const decimal PreMarch2001SmallEngine = 220m;   // up to 1549cc
    private const decimal PreMarch2001LargeEngine = 360m;   // over 1549cc
    private const int SmallEngineThresholdCc = 1549;

    private const decimal Flat2017Standard = 195m;          // petrol, diesel, electric
    private const decimal Flat2017AlternativeFuel = 185m;   // hybrid, bi-fuel, LPG
    private const decimal ExpensiveCarSupplement = 425m;    // list price over £40k, years 2-6

    private static readonly DateTime Co2RegimeStart = new(2001, 3, 1);
    private static readonly DateTime FlatRegimeStart = new(2017, 4, 1);

    /// <summary>CO2 bands for cars first registered 1 Mar 2001 - 31 Mar 2017 (12-month rate).</summary>
    private static readonly (string Band, int MaxCo2, decimal Rate)[] Co2Bands =
    [
        ("A", 100, 20m),
        ("B", 110, 20m),
        ("C", 120, 35m),
        ("D", 130, 165m),
        ("E", 140, 195m),
        ("F", 150, 215m),
        ("G", 165, 265m),
        ("H", 175, 315m),
        ("I", 185, 345m),
        ("J", 200, 395m),
        ("K", 225, 430m),
        ("L", 255, 735m),
        ("M", int.MaxValue, 760m)
    ];

    public static VedEstimate Estimate(VehicleTaxInfo vehicle)
    {
        var firstReg = vehicle.MonthOfFirstRegistration
            ?? (vehicle.YearOfManufacture.HasValue ? new DateTime(vehicle.YearOfManufacture.Value, 1, 1) : null);

        if (firstReg is null)
        {
            return new VedEstimate(null, null, "First registration date unknown",
                "Enter the vehicle on gov.uk to get an exact figure.");
        }

        if (firstReg < Co2RegimeStart)
            return EstimatePreMarch2001(vehicle);

        if (firstReg < FlatRegimeStart)
            return EstimateCo2Band(vehicle, firstReg.Value);

        return EstimateFlatRate(vehicle);
    }

    private static VedEstimate EstimatePreMarch2001(VehicleTaxInfo vehicle)
    {
        const string basis = "Engine size (registered before 1 March 2001)";

        if (vehicle.EngineCapacity is not { } cc || cc <= 0)
            return new VedEstimate(null, null, basis, "Engine size unavailable from DVLA.");

        var rate = cc <= SmallEngineThresholdCc ? PreMarch2001SmallEngine : PreMarch2001LargeEngine;
        var band = cc <= SmallEngineThresholdCc ? "Up to 1549cc" : "Over 1549cc";

        return new VedEstimate(band, rate, basis, null);
    }

    private static VedEstimate EstimateCo2Band(VehicleTaxInfo vehicle, DateTime firstReg)
    {
        const string basis = "CO2 emissions (registered 1 March 2001 - 31 March 2017)";

        // A zero CO2 figure on a combustion car means DVLA holds no reading, not band A.
        var co2 = vehicle.Co2Emissions;
        if (co2 is null || (co2 == 0 && !IsElectric(vehicle.FuelType)))
            return new VedEstimate(null, null, basis, "DVLA holds no CO2 figure for this vehicle.");

        var band = Co2Bands.First(b => co2 <= b.MaxCo2);

        // Band K also covers pre-23 March 2006 cars above 225 g/km.
        if (co2 > 225 && firstReg < new DateTime(2006, 3, 23))
            band = Co2Bands.First(b => b.Band == "K");

        var caveat = IsAlternativeFuel(vehicle.FuelType)
            ? "Alternative-fuel vehicles get a £10 discount on this rate."
            : null;

        return new VedEstimate($"Band {band.Band}", band.Rate, basis, caveat);
    }

    private static VedEstimate EstimateFlatRate(VehicleTaxInfo vehicle)
    {
        const string basis = "Standard rate (registered on or after 1 April 2017)";

        var rate = IsAlternativeFuel(vehicle.FuelType) ? Flat2017AlternativeFuel : Flat2017Standard;

        var caveat = $"Add £{ExpensiveCarSupplement:0}/yr for years 2-6 if the list price was over £40,000 " +
                     "(DVLA does not publish list price, so this cannot be checked automatically).";

        return new VedEstimate("Standard", rate, basis, caveat);
    }

    private static bool IsElectric(string? fuelType) =>
        fuelType is not null &&
        (fuelType.Contains("ELECTRIC", StringComparison.OrdinalIgnoreCase) ||
         fuelType.Contains("FUEL CELL", StringComparison.OrdinalIgnoreCase)) &&
        !fuelType.Contains("HYBRID", StringComparison.OrdinalIgnoreCase) &&
        !fuelType.Contains("DIESEL", StringComparison.OrdinalIgnoreCase);

    private static bool IsAlternativeFuel(string? fuelType)
    {
        if (string.IsNullOrWhiteSpace(fuelType)) return false;
        if (IsElectric(fuelType)) return false;

        return fuelType!.Contains("HYBRID", StringComparison.OrdinalIgnoreCase)
            || fuelType.Contains("GAS", StringComparison.OrdinalIgnoreCase)
            || fuelType.Contains("LPG", StringComparison.OrdinalIgnoreCase)
            || fuelType.Contains("BI-FUEL", StringComparison.OrdinalIgnoreCase)
            || fuelType.Contains("BIOETHANOL", StringComparison.OrdinalIgnoreCase);
    }
}
