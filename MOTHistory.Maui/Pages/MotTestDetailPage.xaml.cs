using MOTHistory.Maui.Models;

namespace MOTHistory.Maui.Pages;

public partial class MotTestDetailPage : ContentPage
{
    public MotTestDetailPage(MotTest test)
    {
        InitializeComponent();
        Populate(test);
    }

    private void Populate(MotTest test)
    {
        Title = test.TestDate.ToString("d MMM yyyy");

        bool passed = test.IsPassed;
        ResultBanner.BackgroundColor = passed ? Color.FromArgb("#34C759") : Color.FromArgb("#FF3B30");
        ResultLabel.Text = passed ? "PASS" : "FAIL";
        DateLabel.Text = test.TestDate.ToString("dddd d MMMM yyyy");

        // Stats row — show whichever combination we have
        bool hasMileage = test.HasOdometer;
        bool hasExpiry = test.HasExpiry;

        if (hasMileage && hasExpiry)
        {
            MileageLabel.Text = test.OdometerDisplay;
            ExpiryLabel.Text = test.ExpiryDate!.Value.ToString("d MMM yyyy");
            MileageRow.IsVisible = true;
        }
        else if (hasMileage)
        {
            MileageOnlyLabel.Text = test.OdometerDisplay;
            MileageOnlyRow.IsVisible = true;
        }
        else if (hasExpiry)
        {
            ExpiryOnlyLabel.Text = test.ExpiryDate!.Value.ToString("d MMM yyyy");
            ExpiryOnlyRow.IsVisible = true;
        }

        if (test.HasNoDefects)
        {
            NoDefectsLabel.IsVisible = true;
        }

        // Failures
        if (test.HasFailures)
        {
            foreach (var text in test.Failures)
                FailuresList.Children.Add(BuildDefectItem(text, Color.FromArgb("#FF3B30")));
            FailuresCard.IsVisible = true;
        }

        // Advisories
        if (test.HasAdvisories)
        {
            foreach (var text in test.Advisories)
                AdvisoriesList.Children.Add(BuildDefectItem(text, Color.FromArgb("#FF9500")));
            AdvisoriesCard.IsVisible = true;
        }
    }

    private static View BuildDefectItem(string text, Color bulletColor)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(new GridLength(20, GridUnitType.Absolute)),
                new ColumnDefinition(GridLength.Star)
            ],
            ColumnSpacing = 4
        };

        var bullet = new Label
        {
            Text = "•",
            TextColor = bulletColor,
            FontSize = 15,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 1, 0, 0)
        };

        var label = new Label
        {
            Text = text,
            FontSize = 14,
            TextColor = Color.FromArgb("#1C1C1E"),
            LineBreakMode = LineBreakMode.WordWrap
        };

        grid.SetColumn(bullet, 0);
        grid.SetColumn(label, 1);
        grid.Children.Add(bullet);
        grid.Children.Add(label);
        return grid;
    }

    private async void OnDoneClicked(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();
}
