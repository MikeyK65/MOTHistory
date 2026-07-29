namespace MOTHistory.Shared;

/// <summary>A single odometer reading, already normalised to miles.</summary>
public record MileageReading(DateTime Date, double Miles);

public enum MileageAnomalyKind
{
    /// <summary>Odometer read lower than an earlier test — the classic "clocking" signature.</summary>
    Rollback,

    /// <summary>An increase too large to be plausible for the elapsed time.</summary>
    ImplausibleJump
}

public record MileageAnomaly(
    MileageAnomalyKind Kind,
    DateTime FromDate,
    DateTime ToDate,
    double FromMiles,
    double ToMiles,
    string Description);

public record MileageReport(
    IReadOnlyList<MileageAnomaly> Anomalies,
    double? AverageAnnualMiles,
    double? TotalRecordedMiles,
    DateTime? FirstReadingDate,
    DateTime? LastReadingDate)
{
    public bool HasAnomalies => Anomalies.Count > 0;

    public bool HasRollback => Anomalies.Any(a => a.Kind == MileageAnomalyKind.Rollback);

    public string? AverageAnnualDisplay =>
        AverageAnnualMiles.HasValue ? $"{AverageAnnualMiles.Value:N0} miles/year" : null;

    /// <summary>
    /// How this vehicle's yearly mileage compares to the UK average, phrased to read
    /// mid-sentence (e.g. "Averages 9,200 miles/year — above the UK average of ...").
    /// </summary>
    public string? UsageComparison
    {
        get
        {
            if (AverageAnnualMiles is not { } avg) return null;

            var ratio = avg / MileageAnalysis.UkAverageAnnualMiles;
            return ratio switch
            {
                < 0.6 => "well below the UK average",
                < 0.9 => "below the UK average",
                <= 1.15 => "about the UK average",
                <= 1.6 => "above the UK average",
                _ => "well above the UK average"
            };
        }
    }

    public static MileageReport Empty { get; } = new([], null, null, null, null);
}

/// <summary>
/// Derives mileage insight from MOT odometer readings — no extra API needed, since the
/// DVSA response already carries a reading per test. Detects rollbacks (a strong used-car
/// warning sign) and implausible jumps, and reports average annual mileage.
/// </summary>
public static class MileageAnalysis
{
    /// <summary>Approximate UK average car mileage per year (DfT National Travel Survey).</summary>
    public const double UkAverageAnnualMiles = 7_400;

    /// <summary>Ignore tiny decreases, which are usually keying slips or same-day retests.</summary>
    private const double RollbackToleranceMiles = 100;

    /// <summary>Flag sustained rates above this as implausible.</summary>
    private const double ImplausibleAnnualRate = 50_000;

    /// <summary>...but only when the absolute jump is also large, so short gaps don't trip it.</summary>
    private const double ImplausibleJumpMinimumMiles = 30_000;

    public static MileageReport Analyse(IEnumerable<MileageReading> readings)
    {
        var ordered = readings
            .Where(r => r.Miles > 0)
            .OrderBy(r => r.Date)
            .ToList();

        if (ordered.Count == 0) return MileageReport.Empty;

        var anomalies = new List<MileageAnomaly>();

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];

            var delta = current.Miles - previous.Miles;
            var days = (current.Date - previous.Date).TotalDays;

            if (delta < -RollbackToleranceMiles)
            {
                anomalies.Add(new MileageAnomaly(
                    MileageAnomalyKind.Rollback,
                    previous.Date, current.Date, previous.Miles, current.Miles,
                    $"Odometer dropped by {Math.Abs(delta):N0} miles between " +
                    $"{previous.Date:d MMM yyyy} ({previous.Miles:N0}) and " +
                    $"{current.Date:d MMM yyyy} ({current.Miles:N0})."));
                continue;
            }

            if (days >= 1 && delta >= ImplausibleJumpMinimumMiles)
            {
                var annualised = delta / days * 365.0;
                if (annualised > ImplausibleAnnualRate)
                {
                    anomalies.Add(new MileageAnomaly(
                        MileageAnomalyKind.ImplausibleJump,
                        previous.Date, current.Date, previous.Miles, current.Miles,
                        $"{delta:N0} miles added in {days:N0} days " +
                        $"({annualised:N0} miles/year) between " +
                        $"{previous.Date:d MMM yyyy} and {current.Date:d MMM yyyy}."));
                }
            }
        }

        var first = ordered[0];
        var last = ordered[^1];
        var spanDays = (last.Date - first.Date).TotalDays;

        // Only report an average over a meaningful span, and never when a rollback makes
        // the first/last difference meaningless.
        double? averageAnnual = null;
        double? total = null;
        if (ordered.Count >= 2 && spanDays >= 180 && last.Miles > first.Miles)
        {
            total = last.Miles - first.Miles;
            averageAnnual = total.Value / spanDays * 365.0;
        }

        return new MileageReport(anomalies, averageAnnual, total, first.Date, last.Date);
    }
}
