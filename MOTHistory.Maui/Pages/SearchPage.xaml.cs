using System.Text;
using System.Text.Json;
using MOTHistory.Maui.Models;
using MOTHistory.Maui.Services;
using MOTHistory.Shared;

namespace MOTHistory.Maui.Pages;

public partial class SearchPage : ContentPage
{
    private readonly DVSAApiService _apiService;
    private readonly DVLAApiService _dvlaService;
    private readonly RecentSearchesService _recentSearches;
    private readonly SavedVehiclesService _savedVehicles;
    private readonly OfflineCacheService _offlineCache;
    private readonly PendingSearchService _pendingSearch;

    private List<MotTest> _currentTests = [];
    private VehicleInfo? _currentVehicle;
    private VehicleTaxInfo? _currentTaxInfo;
    private string? _currentTaxError;
    private MileageReport _currentMileage = MileageReport.Empty;
    private string _currentReg = "";

    public SearchPage()
        : this(IPlatformApplication.Current!.Services.GetRequiredService<DVSAApiService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<DVLAApiService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<RecentSearchesService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<SavedVehiclesService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<OfflineCacheService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<PendingSearchService>()) { }

    public SearchPage(
        DVSAApiService apiService,
        DVLAApiService dvlaService,
        RecentSearchesService recentSearches,
        SavedVehiclesService savedVehicles,
        OfflineCacheService offlineCache,
        PendingSearchService pendingSearch)
    {
        InitializeComponent();
        _apiService = apiService;
        _dvlaService = dvlaService;
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
            // MOT history and DVLA tax data are independent lookups — run them together so
            // adding the second call doesn't slow the search down.
            var motTask = _apiService.GetMotHistoryAsync(reg);
            var dvlaTask = _dvlaService.GetVehicleAsync(reg);
            await Task.WhenAll(motTask, dvlaTask);

            var (json, error) = await motTask;
            ResolveTaxInfo(reg, await dvlaTask);

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

    /// <summary>
    /// Turns the DVLA lookup outcome into <see cref="_currentTaxInfo"/>, falling back to the
    /// cached payload when the call fails so tax data survives going offline. A missing DVLA
    /// key produces no data and no error — the app is fully usable on MOT data alone.
    /// </summary>
    private void ResolveTaxInfo(string reg, (JsonElement? Json, string? Error) result)
    {
        _currentTaxInfo = null;
        _currentTaxError = result.Error;

        if (result.Json.HasValue)
        {
            _offlineCache.Save(reg, result.Json.Value.GetRawText(), OfflineCacheService.DvlaKind);
            _currentTaxInfo = VehicleTaxInfo.Parse(result.Json.Value, reg);
            return;
        }

        var cached = _offlineCache.Load(reg, OfflineCacheService.DvlaKind);
        if (cached is null) return;

        try
        {
            using var doc = JsonDocument.Parse(cached);
            _currentTaxInfo = VehicleTaxInfo.Parse(doc.RootElement, reg);
            if (_currentTaxInfo is not null) _currentTaxError = null;
        }
        catch
        {
            // Corrupt cache — leave tax data unavailable.
        }
    }

    private void ShowResults(JsonElement json, string reg, bool fromCache, DateTime? cachedAt = null)
    {
        _currentReg = reg;
        _currentVehicle = ParseVehicleInfo(json, reg);
        _currentTests = ParseTests(json);
        _currentMileage = MileageAnalysis.Analyse(
            _currentTests
                .Where(t => t.OdometerMiles > 0)
                .Select(t => new MileageReading(t.TestDate, t.OdometerMiles)));

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
            ShowTaxOnlyResults();
            return;
        }

        ShowVehicleInfoCard();
        RefreshSaveButton();

        // Urgent expiry banner — find the current valid MOT expiry
        ShowUrgentBanner(_currentTests);

        // Outstanding manufacturer recall (DVSA), mileage anomalies (derived), tax (DVLA)
        RecallBanner.IsVisible = _currentVehicle?.HasOutstandingRecall == true;
        BuildMileageWarning(_currentMileage);
        BuildTaxCard(_currentTaxInfo, _currentTaxError);

        // Results list — built programmatically so tap gestures work and text never clips
        var count = _currentTests.Count;
        ResultsHeaderLabel.Text = $"Results for {reg}  ·  {count} test{(count == 1 ? "" : "s")}";
        ResultsHeaderLabel.IsVisible = true;
        BuildTestCards(_currentTests);

        // Mileage chart
        var chartPoints = _currentTests.Where(t => t.OdometerMiles > 0).ToList();
        if (chartPoints.Count >= 2)
        {
            var drawable = new MileageChartDrawable { Tests = _currentTests };
            MileageChart.Drawable = drawable;
            MileageChart.Invalidate();
            MileageChartCard.IsVisible = true;

            if (_currentMileage.AverageAnnualDisplay is { } average)
            {
                MileageSummaryLabel.Text =
                    $"Averages {average} — {_currentMileage.UsageComparison} " +
                    $"of {MileageAnalysis.UkAverageAnnualMiles:N0} miles/year.";
                MileageSummaryLabel.IsVisible = true;
            }
            else
            {
                MileageSummaryLabel.IsVisible = false;
            }
        }
        else
        {
            MileageChartCard.IsVisible = false;
        }

        // Defect trends
        BuildDefectTrends(_currentTests);

        ResultsSection.IsVisible = true;
    }

    private void ShowVehicleInfoCard()
    {
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
    }

    /// <summary>
    /// A vehicle under three years old has no MOT tests but still has DVLA tax data, so
    /// show what we do have rather than falling back to a bare "nothing found" state.
    /// </summary>
    private void ShowTaxOnlyResults()
    {
        EmptyStateLayout.IsVisible = true;

        UrgentExpiryBanner.IsVisible = false;
        MileageWarningBanner.IsVisible = false;
        MileageChartCard.IsVisible = false;
        DefectTrendsCard.IsVisible = false;
        TestResultsList.Children.Clear();
        ResultsHeaderLabel.IsVisible = false;

        RecallBanner.IsVisible = _currentVehicle?.HasOutstandingRecall == true;
        BuildTaxCard(_currentTaxInfo, _currentTaxError);
        ShowVehicleInfoCard();
        RefreshSaveButton();

        ResultsSection.IsVisible = TaxCard.IsVisible || VehicleInfoCard.IsVisible || RecallBanner.IsVisible;
    }

    private void ShowUrgentBanner(List<MotTest> tests)
    {
        // Use the latest expiry date across all tests (represents current MOT validity)
        var latestExpiry = tests
            .Where(t => t.ExpiryDate.HasValue)
            .OrderByDescending(t => t.ExpiryDate)
            .FirstOrDefault();

        if (latestExpiry == null || !latestExpiry.IsExpiryUrgent)
        {
            UrgentExpiryBanner.IsVisible = false;
            return;
        }

        var days = latestExpiry.DaysUntilExpiry;
        string title = days switch
        {
            0 => "MOT EXPIRES TODAY",
            1 => "MOT EXPIRES TOMORROW",
            _ => $"MOT EXPIRES IN {days} DAYS"
        };
        UrgentExpiryTitle.Text = title;
        UrgentExpirySubtitle.Text = $"Book your test before {latestExpiry.ExpiryDate!.Value:dddd d MMMM yyyy}";
        UrgentExpiryBanner.BackgroundColor = days <= 7
            ? Color.FromArgb("#FF3B30")
            : Color.FromArgb("#FF9500");
        UrgentExpiryBanner.IsVisible = true;
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

        // Wrap in a container so an urgent strip can be prepended inside the rounded card
        View cardContent = stack;
        if (test.IsExpiryUrgent)
        {
            var days = test.DaysUntilExpiry;
            var urgentText = days switch
            {
                0 => "MOT EXPIRES TODAY",
                1 => "MOT EXPIRES TOMORROW",
                _ => $"MOT EXPIRES IN {days} DAYS"
            };
            var stripColor = days <= 7 ? Color.FromArgb("#FF3B30") : Color.FromArgb("#FF9500");

            var strip = new Grid
            {
                BackgroundColor = stripColor,
                Padding = new Thickness(16, 9),
                Children =
                {
                    new Label
                    {
                        Text = urgentText,
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            };

            var container = new VerticalStackLayout { Spacing = 0 };
            container.Children.Add(strip);
            container.Children.Add(stack);
            cardContent = container;
        }

        var card = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Shadow = new Shadow { Brush = Brush.Black, Offset = new Point(0, 2), Radius = 8, Opacity = 0.07f },
            Margin = new Thickness(16, 6),
            Content = cardContent
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

    /// <summary>
    /// Surfaces odometer rollbacks and implausible jumps. A rollback is the classic
    /// "clocking" signature, so it gets the stronger red treatment.
    /// </summary>
    private void BuildMileageWarning(MileageReport report)
    {
        if (!report.HasAnomalies)
        {
            MileageWarningBanner.IsVisible = false;
            return;
        }

        var severe = report.HasRollback;
        var accent = severe ? Color.FromArgb("#FF3B30") : Color.FromArgb("#FF9500");

        MileageWarningBanner.BackgroundColor = severe ? Color.FromArgb("#FFF0EF") : Color.FromArgb("#FFF6E8");
        MileageWarningBanner.Stroke = accent;

        MileageWarningContent.Children.Clear();
        MileageWarningContent.Children.Add(new Label
        {
            Text = severe ? "MILEAGE DISCREPANCY" : "UNUSUAL MILEAGE",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = accent
        });

        MileageWarningContent.Children.Add(new Label
        {
            Text = severe
                ? "The recorded odometer reading goes down between tests. This can indicate a replaced instrument cluster, a recording error, or that the vehicle has been clocked."
                : "The mileage recorded between tests is unusually high for the time elapsed.",
            FontSize = 13,
            TextColor = accent,
            LineBreakMode = LineBreakMode.WordWrap
        });

        foreach (var anomaly in report.Anomalies)
        {
            MileageWarningContent.Children.Add(new Label
            {
                Text = $"• {anomaly.Description}",
                FontSize = 12,
                TextColor = accent,
                LineBreakMode = LineBreakMode.WordWrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        MileageWarningBanner.IsVisible = true;
    }

    /// <summary>
    /// Builds the DVLA tax/emissions card. Hidden entirely when no DVLA key is configured
    /// and nothing went wrong, so the card never appears as an empty shell.
    /// </summary>
    private void BuildTaxCard(VehicleTaxInfo? tax, string? error)
    {
        TaxCardContent.Children.Clear();

        if (tax is null && string.IsNullOrWhiteSpace(error))
        {
            TaxCard.IsVisible = false;
            return;
        }

        TaxCardContent.Children.Add(new Label
        {
            Text = "TAX & EMISSIONS",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#6E6E73"),
            CharacterSpacing = 0.3
        });

        if (tax is null)
        {
            TaxCardContent.Children.Add(new Label
            {
                Text = error,
                FontSize = 13,
                TextColor = Color.FromArgb("#FF3B30"),
                LineBreakMode = LineBreakMode.WordWrap
            });
            TaxCard.IsVisible = true;
            return;
        }

        // Tax status chip + due date
        var (chipText, chipFg, chipBg) = tax switch
        {
            { IsTaxed: true } => (tax.TaxStatusDisplay.ToUpperInvariant(), Color.FromArgb("#1A7A32"), Color.FromArgb("#E8F8EC")),
            { IsSorn: true } => (tax.TaxStatusDisplay.ToUpperInvariant(), Color.FromArgb("#8E5A00"), Color.FromArgb("#FFF6E8")),
            _ => (tax.TaxStatusDisplay.ToUpperInvariant(), Color.FromArgb("#CC1900"), Color.FromArgb("#FFF0EF"))
        };

        var statusRow = new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(0, 2, 0, 0) };
        statusRow.Children.Add(BuildChip(chipText, chipFg, chipBg));

        if (tax.TaxDueDate.HasValue)
        {
            var overdue = tax.IsTaxOverdue;
            var dueSoon = tax.IsTaxDueSoon;
            statusRow.Children.Add(new Label
            {
                Text = tax.TaxDueDisplay,
                FontSize = 13,
                FontAttributes = overdue || dueSoon ? FontAttributes.Bold : FontAttributes.None,
                TextColor = overdue ? Color.FromArgb("#CC1900")
                          : dueSoon ? Color.FromArgb("#8E5A00")
                          : Color.FromArgb("#6E6E73"),
                VerticalOptions = LayoutOptions.Center
            });
        }

        TaxCardContent.Children.Add(statusRow);

        // Estimated VED
        var ved = VedCalculator.Estimate(tax);
        if (ved.HasRate)
        {
            TaxCardContent.Children.Add(BuildDivider());
            TaxCardContent.Children.Add(BuildDetailRow(
                "Estimated road tax",
                $"{ved.RateDisplay}/yr{(ved.Band is null ? "" : $"  ({ved.Band})")}"));
            TaxCardContent.Children.Add(BuildFootnote(
                $"{ved.Basis}. {VedCalculator.RateTableTaxYear} rates — verify at gov.uk/vehicle-tax-rate-tables."));
            if (ved.Caveat is not null)
                TaxCardContent.Children.Add(BuildFootnote(ved.Caveat));
        }

        // Emissions + ULEZ
        var emissionParts = new List<string>();
        if (!string.IsNullOrEmpty(tax.Co2Display)) emissionParts.Add(tax.Co2Display);
        if (!string.IsNullOrEmpty(tax.EuroStatusDisplay)) emissionParts.Add(tax.EuroStatusDisplay);

        if (emissionParts.Count > 0)
        {
            TaxCardContent.Children.Add(BuildDivider());
            TaxCardContent.Children.Add(BuildDetailRow("Emissions", string.Join(" · ", emissionParts)));
        }

        var ulez = UlezChecker.Check(tax);
        if (ulez.Verdict != UlezVerdict.Unknown)
        {
            if (emissionParts.Count == 0) TaxCardContent.Children.Add(BuildDivider());
            TaxCardContent.Children.Add(BuildDetailRow(
                "ULEZ",
                ulez.VerdictDisplay,
                ulez.Verdict == UlezVerdict.Compliant ? Color.FromArgb("#1A7A32") : Color.FromArgb("#CC1900")));
            TaxCardContent.Children.Add(BuildFootnote(
                $"{ulez.Reason} Estimate only — confirm at tfl.gov.uk."));
        }

        // Provenance flags
        if (tax.MarkedForExport)
        {
            TaxCardContent.Children.Add(BuildDivider());
            TaxCardContent.Children.Add(BuildDetailRow("Export marker", "Marked for export", Color.FromArgb("#CC1900")));
        }

        if (tax.V5CRecentlyIssued)
        {
            TaxCardContent.Children.Add(BuildDivider());
            TaxCardContent.Children.Add(BuildDetailRow(
                "Last V5C issued",
                tax.DateOfLastV5CIssued!.Value.ToString("d MMM yyyy")));
            TaxCardContent.Children.Add(BuildFootnote(
                "A recently issued logbook often means the vehicle has changed keeper."));
        }

        TaxCard.IsVisible = true;
    }

    private static View BuildDivider() => new BoxView
    {
        HeightRequest = 1,
        BackgroundColor = Color.FromArgb("#C6C6C8"),
        Margin = new Thickness(0, 6)
    };

    private static View BuildDetailRow(string label, string value, Color? valueColor = null)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            ],
            ColumnSpacing = 12
        };

        var labelView = new Label
        {
            Text = label,
            FontSize = 13,
            TextColor = Color.FromArgb("#6E6E73"),
            VerticalOptions = LayoutOptions.Center
        };

        var valueView = new Label
        {
            Text = value,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = valueColor ?? Color.FromArgb("#1C1C1E"),
            HorizontalTextAlignment = TextAlignment.End,
            LineBreakMode = LineBreakMode.WordWrap,
            VerticalOptions = LayoutOptions.Center
        };

        row.SetColumn(labelView, 0);
        row.SetColumn(valueView, 1);
        row.Children.Add(labelView);
        row.Children.Add(valueView);
        return row;
    }

    private static View BuildFootnote(string text) => new Label
    {
        Text = text,
        FontSize = 11,
        TextColor = Color.FromArgb("#AEAEB2"),
        LineBreakMode = LineBreakMode.WordWrap
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

        if (_currentVehicle?.HasOutstandingRecall == true)
            sb.AppendLine("WARNING: outstanding manufacturer safety recall.");

        if (_currentTaxInfo is { } tax)
        {
            sb.AppendLine($"Tax: {tax.TaxStatusDisplay}{(tax.TaxDueDate.HasValue ? $" ({tax.TaxDueDisplay})" : "")}");
            if (!string.IsNullOrEmpty(tax.Co2Display)) sb.AppendLine($"CO2: {tax.Co2Display}");
        }

        if (_currentMileage.HasRollback)
            sb.AppendLine("WARNING: odometer reading decreases between tests.");

        if (_currentMileage.AverageAnnualDisplay is { } average)
            sb.AppendLine($"Average use: {average}");

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

        // hasOutstandingRecall is documented as a string ("Yes"/"No"/"Unknown"), but accept
        // a JSON boolean too in case DVSA changes the representation.
        string GetFlag(string key)
        {
            if (!v.TryGetProperty(key, out var p)) return "";
            return p.ValueKind switch
            {
                JsonValueKind.String => p.GetString() ?? "",
                JsonValueKind.True => "Yes",
                JsonValueKind.False => "No",
                _ => ""
            };
        }

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
            FirstUsedDate = firstUsed,
            OutstandingRecall = GetFlag("hasOutstandingRecall")
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
