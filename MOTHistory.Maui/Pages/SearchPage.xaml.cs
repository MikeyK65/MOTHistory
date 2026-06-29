using System.Text;
using System.Text.Json;
using MOTHistory.Maui.Models;
using MOTHistory.Maui.Services;

namespace MOTHistory.Maui.Pages;

public partial class SearchPage : ContentPage
{
    private readonly DVSAApiService _apiService;
    private readonly RecentSearchesService _recentSearches;
    private readonly SavedVehiclesService _savedVehicles;
    private readonly OfflineCacheService _offlineCache;
    private readonly PendingSearchService _pendingSearch;

    private List<MotTest> _currentTests = [];
    private VehicleInfo? _currentVehicle;
    private string _currentReg = "";

    public SearchPage()
        : this(IPlatformApplication.Current!.Services.GetRequiredService<DVSAApiService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<RecentSearchesService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<SavedVehiclesService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<OfflineCacheService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<PendingSearchService>()) { }

    public SearchPage(
        DVSAApiService apiService,
        RecentSearchesService recentSearches,
        SavedVehiclesService savedVehicles,
        OfflineCacheService offlineCache,
        PendingSearchService pendingSearch)
    {
        InitializeComponent();
        _apiService = apiService;
        _recentSearches = recentSearches;
        _savedVehicles = savedVehicles;
        _offlineCache = offlineCache;
        _pendingSearch = pendingSearch;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_pendingSearch.PendingRegistration is { } pending)
        {
            _pendingSearch.PendingRegistration = null;
            RegistrationEntry.Text = pending;
            await RunSearchAsync();
            return;
        }

        RefreshSaveButton();
        ShowRecentSearches();
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

        reg = reg.ToUpperInvariant().Replace(" ", "");
        SetLoadingState(true);

        try
        {
            var (json, error) = await _apiService.GetMotHistoryAsync(reg);

            if (json.HasValue)
            {
                _offlineCache.Save(reg, json.Value.GetRawText());
                _recentSearches.Add(reg);
                ShowResults(json.Value, reg, fromCache: false);
            }
            else
            {
                // Try offline cache before showing error
                var cached = _offlineCache.Load(reg);
                if (cached != null)
                {
                    using var doc = JsonDocument.Parse(cached);
                    var cachedJson = doc.RootElement.Clone();
                    _recentSearches.Add(reg);
                    ShowResults(cachedJson, reg, fromCache: true, cachedAt: _offlineCache.CachedAt(reg));
                }
                else
                {
                    ShowError(error ?? "Unknown error.");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    private void ShowResults(JsonElement json, string reg, bool fromCache, DateTime? cachedAt = null)
    {
        _currentReg = reg;
        _currentVehicle = ParseVehicleInfo(json, reg);
        _currentTests = ParseTests(json);

        ErrorBorder.IsVisible = false;
        RecentSearchesSection.IsVisible = false;
        EmptyStateLayout.IsVisible = false;

        if (fromCache && cachedAt.HasValue)
        {
            CacheBannerLabel.Text = $"Showing cached results from {cachedAt.Value:d MMM yyyy} — no internet connection.";
            CacheBanner.IsVisible = true;
        }
        else
        {
            CacheBanner.IsVisible = false;
        }

        if (_currentTests.Count == 0)
        {
            ResultsSection.IsVisible = false;
            EmptyStateLayout.IsVisible = true;
            return;
        }

        // Vehicle info
        if (_currentVehicle?.HasDetail == true)
        {
            VehicleMakeModelLabel.Text = _currentVehicle.MakeModel;
            VehicleColourFuelLabel.Text = _currentVehicle.ColourFuelEngine;
            VehicleFirstUsedLabel.Text = _currentVehicle.FirstUsedDisplay;
            VehicleInfoCard.IsVisible = true;
        }
        else
        {
            VehicleInfoCard.IsVisible = false;
        }

        RefreshSaveButton();

        // Results list — built programmatically so tap gestures work and text never clips
        var count = _currentTests.Count;
        ResultsHeaderLabel.Text = $"Results for {reg}  ·  {count} test{(count == 1 ? "" : "s")}";
        BuildTestCards(_currentTests);

        // Mileage chart
        var chartPoints = _currentTests.Where(t => t.OdometerMiles > 0).ToList();
        if (chartPoints.Count >= 2)
        {
            var drawable = new MileageChartDrawable { Tests = _currentTests };
            MileageChart.Drawable = drawable;
            MileageChart.Invalidate();
            MileageChartCard.IsVisible = true;
        }
        else
        {
            MileageChartCard.IsVisible = false;
        }

        // Defect trends
        BuildDefectTrends(_currentTests);

        ResultsSection.IsVisible = true;
    }

    private void BuildTestCards(List<MotTest> tests)
    {
        TestResultsList.Children.Clear();
        foreach (var test in tests)
            TestResultsList.Children.Add(BuildTestCard(test));
    }

    private View BuildTestCard(MotTest test)
    {
        bool passed = test.IsPassed;
        var badgeColor = passed ? Color.FromArgb("#34C759") : Color.FromArgb("#FF3B30");

        // Date label
        var dateLabel = new Label
        {
            Text = test.TestDate.ToString("d MMM yyyy"),
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1C1C1E"),
            VerticalOptions = LayoutOptions.Center
        };

        // Pass/Fail badge
        var badge = new Border
        {
            BackgroundColor = badgeColor,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 5),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = test.TestResult,
                TextColor = Colors.White,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold
            }
        };

        var headerRow = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            ],
            ColumnSpacing = 10
        };
        headerRow.SetColumn(dateLabel, 0);
        headerRow.SetColumn(badge, 1);
        headerRow.Children.Add(dateLabel);
        headerRow.Children.Add(badge);

        var stack = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(16) };
        stack.Children.Add(headerRow);

        // Mileage + expiry
        if (test.HasOdometer)
            stack.Children.Add(new Label
            {
                Text = test.OdometerDisplay,
                FontSize = 14,
                TextColor = Color.FromArgb("#6E6E73"),
                Margin = new Thickness(0, 6, 0, 0)
            });

        if (test.HasExpiry)
            stack.Children.Add(new Label
            {
                Text = test.ExpiryDisplay,
                FontSize = 14,
                TextColor = Color.FromArgb("#6E6E73"),
                Margin = new Thickness(0, 2, 0, 0)
            });

        // Defect summary chips
        if (test.HasFailures || test.HasAdvisories || test.HasNoDefects)
        {
            stack.Children.Add(new BoxView
            {
                HeightRequest = 1,
                BackgroundColor = Color.FromArgb("#C6C6C8"),
                Margin = new Thickness(0, 10, 0, 8)
            });
        }

        if (test.HasNoDefects)
        {
            stack.Children.Add(new Label
            {
                Text = "No defects recorded",
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#34C759")
            });
        }
        else
        {
            var chipRow = new HorizontalStackLayout { Spacing = 8 };

            if (test.HasFailures)
                chipRow.Children.Add(BuildChip(
                    $"{test.Failures.Count} failure{(test.Failures.Count == 1 ? "" : "s")}",
                    Color.FromArgb("#FF3B30"), Color.FromArgb("#FFF0EF")));

            if (test.HasAdvisories)
                chipRow.Children.Add(BuildChip(
                    $"{test.Advisories.Count} advisor{(test.Advisories.Count == 1 ? "y" : "ies")}",
                    Color.FromArgb("#FF9500"), Color.FromArgb("#FFF6E8")));

            stack.Children.Add(chipRow);
        }

        // "Tap for details" hint
        var hint = new Label
        {
            Text = "Tap to view full details",
            FontSize = 12,
            TextColor = Color.FromArgb("#AEAEB2"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalOptions = LayoutOptions.End
        };
        stack.Children.Add(hint);

        var card = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Shadow = new Shadow { Brush = Brush.Black, Offset = new Point(0, 2), Radius = 8, Opacity = 0.07f },
            Margin = new Thickness(16, 6),
            Content = stack
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
            await Navigation.PushModalAsync(
                new NavigationPage(new MotTestDetailPage(test))
                {
                    BarBackgroundColor = Color.FromArgb("#007AFF"),
                    BarTextColor = Colors.White
                });
        card.GestureRecognizers.Add(tap);

        return card;
    }

    private static View BuildChip(string text, Color textColor, Color bgColor) =>
        new Border
        {
            BackgroundColor = bgColor,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 4),
            Content = new Label
            {
                Text = text,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor
            }
        };

    private void BuildDefectTrends(List<MotTest> tests)
    {
        var recurring = tests
            .SelectMany(t => t.Advisories.Concat(t.Failures))
            .GroupBy(d => d.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => (Text: g.Key, Count: g.Count()))
            .ToList();

        if (recurring.Count == 0)
        {
            DefectTrendsCard.IsVisible = false;
            return;
        }

        DefectTrendsContent.Children.Clear();
        DefectTrendsContent.Children.Add(new Label
        {
            Text = "RECURRING ISSUES",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#6E6E73"),
            CharacterSpacing = 0.3
        });

        foreach (var (text, count) in recurring)
        {
            var row = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                ],
                ColumnSpacing = 8,
                Margin = new Thickness(0, 2)
            };

            var textLabel = new Label
            {
                Text = text,
                FontSize = 13,
                TextColor = Color.FromArgb("#1C1C1E"),
                LineBreakMode = LineBreakMode.WordWrap,
                VerticalOptions = LayoutOptions.Center
            };

            var badge = new Border
            {
                BackgroundColor = Color.FromArgb("#FF9500"),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(8, 3),
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = $"x{count}",
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White
                }
            };

            row.SetColumn(textLabel, 0);
            row.SetColumn(badge, 1);
            row.Children.Add(textLabel);
            row.Children.Add(badge);

            DefectTrendsContent.Children.Add(row);
        }

        DefectTrendsCard.IsVisible = true;
    }

    private void ShowRecentSearches()
    {
        var recents = _recentSearches.Get();
        if (recents.Count == 0 || ResultsSection.IsVisible)
        {
            RecentSearchesSection.IsVisible = false;
            return;
        }

        RecentSearchesList.Children.Clear();
        bool first = true;
        foreach (var reg in recents)
        {
            if (!first)
            {
                RecentSearchesList.Children.Add(new BoxView
                {
                    HeightRequest = 1,
                    BackgroundColor = Color.FromArgb("#C6C6C8"),
                    Margin = new Thickness(16, 0)
                });
            }
            first = false;

            var row = new Grid
            {
                Padding = new Thickness(16, 12),
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                ]
            };

            var regLabel = new Label
            {
                Text = reg,
                FontSize = 17,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1C1C1E"),
                VerticalOptions = LayoutOptions.Center
            };

            var chevron = new Label
            {
                Text = ">",
                FontSize = 15,
                TextColor = Color.FromArgb("#C6C6C8"),
                VerticalOptions = LayoutOptions.Center
            };

            row.SetColumn(regLabel, 0);
            row.SetColumn(chevron, 1);
            row.Children.Add(regLabel);
            row.Children.Add(chevron);

            var tapReg = reg;
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                RegistrationEntry.Text = tapReg;
                await RunSearchAsync();
            };
            row.GestureRecognizers.Add(tap);
            RecentSearchesList.Children.Add(row);
        }

        RecentSearchesSection.IsVisible = true;
    }

    private void RefreshSaveButton()
    {
        if (string.IsNullOrEmpty(_currentReg))
        {
            SaveButton.IsEnabled = false;
            return;
        }

        SaveButton.IsEnabled = true;
        SaveButton.Text = _savedVehicles.IsSaved(_currentReg) ? "Saved" : "Save to Garage";
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_currentReg)) return;

        if (_savedVehicles.IsSaved(_currentReg))
        {
            bool remove = await DisplayAlertAsync("Already Saved",
                $"{_currentReg} is already in your Garage. Remove it?",
                "Remove", "Keep");
            if (remove)
            {
                _savedVehicles.Remove(_currentReg);
                SaveButton.Text = "Save to Garage";
            }
            return;
        }

        var nickname = await DisplayPromptAsync(
            "Save to Garage",
            "Give this vehicle a nickname (optional):",
            placeholder: _currentVehicle?.MakeModel ?? "",
            maxLength: 40);

        var latestExpiry = _currentTests
            .Where(t => t.ExpiryDate.HasValue)
            .Select(t => t.ExpiryDate!.Value)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        _savedVehicles.Save(new SavedVehicle
        {
            Registration = _currentReg,
            Nickname = nickname?.Trim() ?? "",
            Make = _currentVehicle?.Make ?? "",
            Model = _currentVehicle?.Model ?? "",
            MotExpiryDate = latestExpiry == default ? null : latestExpiry,
            SavedAt = DateTime.UtcNow
        });

        SaveButton.Text = "Saved";
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        if (_currentTests.Count == 0 || string.IsNullOrEmpty(_currentReg)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"MOT History for {_currentReg}");
        if (_currentVehicle?.HasDetail == true)
            sb.AppendLine(_currentVehicle.MakeModel);
        sb.AppendLine();

        foreach (var t in _currentTests)
        {
            sb.AppendLine($"{t.TestDate:d MMM yyyy}  {t.TestResult}");
            if (t.HasOdometer) sb.AppendLine($"  Mileage: {t.OdometerDisplay}");
            if (t.HasExpiry) sb.AppendLine($"  {t.ExpiryDisplay}");
            if (t.HasFailures)
            {
                sb.AppendLine("  Failures:");
                foreach (var f in t.Failures) sb.AppendLine($"    - {f}");
            }
            if (t.HasAdvisories)
            {
                sb.AppendLine("  Advisories:");
                foreach (var a in t.Advisories) sb.AppendLine($"    - {a}");
            }
            sb.AppendLine();
        }

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = $"MOT History – {_currentReg}",
            Text = sb.ToString()
        });
    }

    private void SetLoadingState(bool loading)
    {
        SearchButton.IsEnabled = !loading;
        LoadingIndicator.IsVisible = loading;
        LoadingIndicator.IsRunning = loading;
        if (loading)
        {
            ErrorBorder.IsVisible = false;
            CacheBanner.IsVisible = false;
            RecentSearchesSection.IsVisible = false;
            ResultsSection.IsVisible = false;
            EmptyStateLayout.IsVisible = false;
        }
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorBorder.IsVisible = true;
        ResultsSection.IsVisible = false;
        EmptyStateLayout.IsVisible = false;
        ShowRecentSearches();
    }

    private static VehicleInfo ParseVehicleInfo(JsonElement json, string registration)
    {
        var v = json.ValueKind == JsonValueKind.Array && json.GetArrayLength() > 0 ? json[0] : json;

        string Get(string key) =>
            v.TryGetProperty(key, out var p) && p.ValueKind != JsonValueKind.Null
                ? p.GetString() ?? "" : "";

        DateTime? firstUsed = null;
        if (v.TryGetProperty("firstUsedDate", out var fud) && fud.ValueKind != JsonValueKind.Null)
            if (DateTime.TryParse(fud.GetString(), out var dt)) firstUsed = dt;

        return new VehicleInfo
        {
            Registration = registration,
            Make = Get("make"),
            Model = Get("model"),
            Colour = Get("primaryColour"),
            FuelType = Get("fuelType"),
            EngineSize = Get("engineSize"),
            FirstUsedDate = firstUsed
        };
    }

    private static List<MotTest> ParseTests(JsonElement json)
    {
        var v = json.ValueKind == JsonValueKind.Array && json.GetArrayLength() > 0 ? json[0] : json;
        var results = new List<MotTest>();

        if (!v.TryGetProperty("motTests", out var tests) || tests.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var t in tests.EnumerateArray())
        {
            var test = new MotTest
            {
                TestDate = t.TryGetProperty("completedDate", out var cd) ? cd.GetDateTime() : default,
                TestResult = t.TryGetProperty("testResult", out var tr) ? (tr.GetString() ?? "") : "",
                ExpiryDate = t.TryGetProperty("expiryDate", out var exp) && exp.ValueKind != JsonValueKind.Null
                    ? exp.GetDateTime() : null,
                OdometerValue = t.TryGetProperty("odometerValue", out var odo) ? (odo.GetString() ?? "") : "",
                OdometerUnit = t.TryGetProperty("odometerUnit", out var unit) ? (unit.GetString() ?? "") : ""
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
}
