using System.Text.Json;
using MOTHistory.Maui.Models;
using MOTHistory.Maui.Services;

namespace MOTHistory.Maui.Pages;

public partial class SearchPage : ContentPage
{
    private readonly DVSAApiService _apiService;

    // Parameterless ctor for Shell DataTemplate (Activator.CreateInstance path)
    public SearchPage() : this(IPlatformApplication.Current!.Services.GetRequiredService<DVSAApiService>()) { }

    public SearchPage(DVSAApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
    }

    private void OnRegistrationCompleted(object? sender, EventArgs e) => _ = RunSearchAsync();

    private void OnSearchClicked(object? sender, EventArgs e) => _ = RunSearchAsync();

    private async Task RunSearchAsync()
    {
        var reg = RegistrationEntry.Text?.Trim();
        if (string.IsNullOrEmpty(reg))
        {
            ShowError("Please enter a registration number.");
            return;
        }

        SetLoadingState(true);

        try
        {
            var (json, error) = await _apiService.GetMotHistoryAsync(reg);

            if (error is not null)
            {
                ShowError(error);
                return;
            }

            if (json is null)
            {
                ShowError("No MOT history found or unexpected API response.");
                return;
            }

            var tests = ParseTests(json.Value);
            ShowResults(tests, reg.ToUpperInvariant());
        }
        catch (OperationCanceledException)
        {
            // Ignored — user navigated away
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    private static List<MotTest> ParseTests(JsonElement json)
    {
        JsonElement vehicle;
        if (json.ValueKind == JsonValueKind.Array)
        {
            if (json.GetArrayLength() == 0) return new List<MotTest>();
            vehicle = json[0];
        }
        else
        {
            vehicle = json;
        }

        var results = new List<MotTest>();

        if (!vehicle.TryGetProperty("motTests", out var tests) || tests.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var t in tests.EnumerateArray())
        {
            var test = new MotTest
            {
                TestDate = t.TryGetProperty("completedDate", out var cd) ? cd.GetDateTime() : default,
                TestResult = t.TryGetProperty("testResult", out var tr) ? (tr.GetString() ?? "") : "",
                ExpiryDate = t.TryGetProperty("expiryDate", out var exp) && exp.ValueKind != JsonValueKind.Null
                    ? exp.GetDateTime()
                    : (DateTime?)null,
                OdometerValue = t.TryGetProperty("odometerValue", out var odo) ? (odo.GetString() ?? "") : "",
                OdometerUnit = t.TryGetProperty("odometerUnit", out var unit) ? (unit.GetString() ?? "") : "",
            };

            if (t.TryGetProperty("defects", out var defects) && defects.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in defects.EnumerateArray())
                {
                    var type = d.TryGetProperty("type", out var dt) ? dt.GetString() : null;
                    var text = d.TryGetProperty("text", out var txt) ? txt.GetString() : null;
                    if (type is "FAIL" or "MAJOR" or "DANGEROUS") test.Failures.Add(text ?? "");
                    else if (type is "ADVISORY" or "MINOR") test.Advisories.Add(text ?? "");
                }
            }

            results.Add(test);
        }

        return results;
    }

    private void SetLoadingState(bool loading)
    {
        SearchButton.IsEnabled = !loading;
        LoadingIndicator.IsVisible = loading;
        LoadingIndicator.IsRunning = loading;
        if (loading)
        {
            ErrorBorder.IsVisible = false;
            ResultsHeaderLabel.IsVisible = false;
            ResultsView.IsVisible = false;
            EmptyStateLayout.IsVisible = false;
        }
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorBorder.IsVisible = true;
        ResultsHeaderLabel.IsVisible = false;
        ResultsView.IsVisible = false;
        EmptyStateLayout.IsVisible = false;
    }

    private void ShowResults(List<MotTest> tests, string reg)
    {
        ErrorBorder.IsVisible = false;

        if (tests.Count == 0)
        {
            ResultsHeaderLabel.IsVisible = false;
            ResultsView.IsVisible = false;
            EmptyStateLayout.IsVisible = true;
            return;
        }

        var count = tests.Count;
        ResultsHeaderLabel.Text = $"Results for {reg}  ·  {count} test{(count == 1 ? "" : "s")}";
        ResultsHeaderLabel.IsVisible = true;
        ResultsView.ItemsSource = tests;
        ResultsView.IsVisible = true;
        EmptyStateLayout.IsVisible = false;
    }
}
