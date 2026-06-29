using MOTHistory.Maui.Models;
using MOTHistory.Maui.Services;

namespace MOTHistory.Maui.Pages;

public partial class GaragePage : ContentPage
{
    private readonly SavedVehiclesService _savedVehicles;
    private readonly PendingSearchService _pendingSearch;

    public GaragePage()
        : this(IPlatformApplication.Current!.Services.GetRequiredService<SavedVehiclesService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<PendingSearchService>()) { }

    public GaragePage(SavedVehiclesService savedVehicles, PendingSearchService pendingSearch)
    {
        InitializeComponent();
        _savedVehicles = savedVehicles;
        _pendingSearch = pendingSearch;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshList();
    }

    private void RefreshList()
    {
        var vehicles = _savedVehicles.GetAll();
        VehicleListLayout.Children.Clear();

        if (vehicles.Count == 0)
        {
            EmptyStateLayout.IsVisible = true;
            VehicleListLayout.IsVisible = false;
            return;
        }

        EmptyStateLayout.IsVisible = false;
        VehicleListLayout.IsVisible = true;

        foreach (var vehicle in vehicles)
            VehicleListLayout.Children.Add(BuildVehicleCard(vehicle));
    }

    private View BuildVehicleCard(SavedVehicle vehicle)
    {
        var expiryColor = vehicle.IsExpired ? Color.FromArgb("#FF3B30")
            : vehicle.IsExpiringSoon ? Color.FromArgb("#FF9500")
            : Color.FromArgb("#34C759");

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#FFFFFF"),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Shadow = new Shadow { Brush = Brush.Black, Offset = new Point(0, 2), Radius = 8, Opacity = 0.07f }
        };

        var titleLabel = new Label
        {
            Text = vehicle.DisplayName,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1C1C1E")
        };

        var subLabel = new Label
        {
            Text = vehicle.SubTitle,
            FontSize = 14,
            TextColor = Color.FromArgb("#6E6E73"),
            IsVisible = vehicle.SubTitle != vehicle.DisplayName || !string.IsNullOrWhiteSpace(vehicle.SubTitle)
        };

        var expiryDot = new BoxView
        {
            WidthRequest = 10,
            HeightRequest = 10,
            CornerRadius = 5,
            BackgroundColor = expiryColor,
            VerticalOptions = LayoutOptions.Center
        };

        var expiryLabel = new Label
        {
            Text = vehicle.ExpiryDisplay,
            FontSize = 13,
            TextColor = expiryColor,
            FontAttributes = vehicle.IsExpired || vehicle.IsExpiringSoon ? FontAttributes.Bold : FontAttributes.None,
            VerticalOptions = LayoutOptions.Center
        };

        var expiryRow = new HorizontalStackLayout
        {
            Spacing = 6,
            Margin = new Thickness(0, 4, 0, 0),
            Children = { expiryDot, expiryLabel }
        };

        var searchBtn = new Button
        {
            Text = "Search",
            BackgroundColor = Color.FromArgb("#007AFF"),
            TextColor = Colors.White,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            HeightRequest = 40,
            HorizontalOptions = LayoutOptions.Fill
        };
        searchBtn.Clicked += (_, _) => OnSearchVehicle(vehicle);

        var removeBtn = new Button
        {
            Text = "Remove",
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#FF3B30"),
            BorderColor = Color.FromArgb("#FF3B30"),
            BorderWidth = 1.5,
            FontSize = 15,
            CornerRadius = 10,
            HeightRequest = 40,
            HorizontalOptions = LayoutOptions.Fill
        };
        removeBtn.Clicked += (_, _) => OnRemoveVehicle(vehicle);

        var buttonRow = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            ],
            ColumnSpacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttonRow.SetColumn(searchBtn, 0);
        buttonRow.SetColumn(removeBtn, 1);
        buttonRow.Children.Add(searchBtn);
        buttonRow.Children.Add(removeBtn);

        var stack = new VerticalStackLayout
        {
            Padding = new Thickness(16),
            Spacing = 0,
            Children = { titleLabel, subLabel, expiryRow, buttonRow }
        };

        card.Content = stack;
        return card;
    }

    private async void OnSearchVehicle(SavedVehicle vehicle)
    {
        _pendingSearch.PendingRegistration = vehicle.Registration;
        await Shell.Current.GoToAsync("//SearchPage");
    }

    private async void OnRemoveVehicle(SavedVehicle vehicle)
    {
        bool confirmed = await DisplayAlertAsync(
            "Remove Vehicle",
            $"Remove {vehicle.DisplayName} from your Garage?",
            "Remove", "Cancel");

        if (!confirmed) return;

        _savedVehicles.Remove(vehicle.Registration);
        RefreshList();
    }
}
