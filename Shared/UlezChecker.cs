namespace MOTHistory.Shared;

public enum UlezVerdict { Compliant, NotCompliant, Unknown }

public record UlezResult(UlezVerdict Verdict, string Reason)
{
    public string VerdictDisplay => Verdict switch
    {
        UlezVerdict.Compliant => "Likely ULEZ compliant",
        UlezVerdict.NotCompliant => "Likely NOT ULEZ compliant",
        _ => "ULEZ status unknown"
    };
}

/// <summary>
/// Indicative London ULEZ / Clean Air Zone check based on the Euro emissions standard
/// DVLA holds, falling back to first-registration date when no Euro status is published.
///
/// This is an ESTIMATE only. TfL's own vehicle checker is the authoritative source and
/// callers should always link to it alongside this result:
/// https://tfl.gov.uk/modes/driving/check-your-vehicle
/// </summary>
public static class UlezChecker
{
    // Minimum Euro standard for ULEZ compliance.
    private const int PetrolMinimumEuro = 4;
    private const int DieselMinimumEuro = 6;

    // Approximate dates by which nearly all new cars met the standard above.
    private static readonly DateTime PetrolEuro4Cutoff = new(2006, 1, 1);
    private static readonly DateTime DieselEuro6Cutoff = new(2015, 9, 1);

    public static UlezResult Check(VehicleTaxInfo vehicle)
    {
        var fuel = vehicle.FuelType ?? "";

        if (IsZeroEmission(fuel))
            return new UlezResult(UlezVerdict.Compliant, "Fully electric and hydrogen vehicles are always compliant.");

        var isDiesel = fuel.Contains("DIESEL", StringComparison.OrdinalIgnoreCase);
        var isPetrol = fuel.Contains("PETROL", StringComparison.OrdinalIgnoreCase)
                    || fuel.Contains("HYBRID", StringComparison.OrdinalIgnoreCase)
                    || fuel.Contains("GAS", StringComparison.OrdinalIgnoreCase);

        if (!isDiesel && !isPetrol)
            return new UlezResult(UlezVerdict.Unknown, $"Cannot assess fuel type '{fuel}'. Check with TfL.");

        var required = isDiesel ? DieselMinimumEuro : PetrolMinimumEuro;
        var fuelName = isDiesel ? "Diesel" : "Petrol";

        if (TryParseEuroStandard(vehicle.EuroStatus, out var euro))
        {
            return euro >= required
                ? new UlezResult(UlezVerdict.Compliant,
                    $"{fuelName} rated Euro {euro}; ULEZ requires Euro {required} or better.")
                : new UlezResult(UlezVerdict.NotCompliant,
                    $"{fuelName} rated Euro {euro}; ULEZ requires Euro {required} or better.");
        }

        // No Euro status published — fall back to registration date, which is less reliable.
        var firstReg = vehicle.MonthOfFirstRegistration;
        if (firstReg is null)
            return new UlezResult(UlezVerdict.Unknown, "DVLA holds no Euro status or registration date for this vehicle.");

        var cutoff = isDiesel ? DieselEuro6Cutoff : PetrolEuro4Cutoff;

        return firstReg >= cutoff
            ? new UlezResult(UlezVerdict.Compliant,
                $"No Euro status published; estimated from a {firstReg.Value:MMMM yyyy} registration " +
                $"(most {fuelName.ToLowerInvariant()} cars from {cutoff:MMMM yyyy} meet Euro {required}).")
            : new UlezResult(UlezVerdict.NotCompliant,
                $"No Euro status published; estimated from a {firstReg.Value:MMMM yyyy} registration " +
                $"(most {fuelName.ToLowerInvariant()} cars before {cutoff:MMMM yyyy} predate Euro {required}).");
    }

    private static bool IsZeroEmission(string fuel) =>
        (fuel.Contains("ELECTRIC", StringComparison.OrdinalIgnoreCase) ||
         fuel.Contains("FUEL CELL", StringComparison.OrdinalIgnoreCase)) &&
        !fuel.Contains("HYBRID", StringComparison.OrdinalIgnoreCase) &&
        !fuel.Contains("DIESEL", StringComparison.OrdinalIgnoreCase) &&
        !fuel.Contains("PETROL", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pulls the leading number out of values like "6", "6b", "EURO 4" or "W".</summary>
    private static bool TryParseEuroStandard(string? euroStatus, out int euro)
    {
        euro = 0;
        if (string.IsNullOrWhiteSpace(euroStatus)) return false;

        var digits = new string(euroStatus
            .SkipWhile(c => !char.IsDigit(c))
            .TakeWhile(char.IsDigit)
            .ToArray());

        return digits.Length > 0 && int.TryParse(digits, out euro);
    }
}
