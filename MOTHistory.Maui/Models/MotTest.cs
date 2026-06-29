namespace MOTHistory.Maui.Models;

public class MotTest
{
    public DateTime TestDate { get; set; }
    public string TestResult { get; set; } = "";
    public DateTime? ExpiryDate { get; set; }
    public string OdometerValue { get; set; } = "";
    public string OdometerUnit { get; set; } = "";
    public List<string> Failures { get; set; } = new();
    public List<string> Advisories { get; set; } = new();

    public bool IsPassed => TestResult.Contains("PASS", StringComparison.OrdinalIgnoreCase);
    public bool HasFailures => Failures.Count > 0;
    public bool HasAdvisories => Advisories.Count > 0;
    public bool HasNoDefects => !HasFailures && !HasAdvisories;
    public bool HasDefects => HasFailures || HasAdvisories;
    public bool HasExpiry => ExpiryDate.HasValue;

    public string OdometerDisplay =>
        string.IsNullOrWhiteSpace(OdometerValue) ? "" : $"{OdometerValue} {OdometerUnit}".Trim();

    public bool HasOdometer => !string.IsNullOrWhiteSpace(OdometerDisplay);

    public string ExpiryDisplay =>
        ExpiryDate.HasValue ? $"Expires {ExpiryDate.Value:d MMM yyyy}" : "";
}
