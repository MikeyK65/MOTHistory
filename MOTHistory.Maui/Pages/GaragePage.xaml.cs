using MOTHistory.Maui.Models;
using MOTHistory.Maui.Services;

namespace MOTHistory.Maui.Pages;

public partial class GaragePage : ContentPage
{
    private readonly SavedVehiclesService _savedVehicles;
    private readonly PendingSearchService _pendingSearch;
    private readonly NotificationService _notifications;

    public GaragePage()
        : this(IPlatformApplication.Current!.Services.GetRequiredService<SavedVehiclesService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<PendingSearchService>(),
               IPlatformApplication.Current!.Services.GetRequiredService<NotificationService>()) { }

    public GaragePage(SavedVehiclesService savedVehicles, PendingSearchService pendingSearch, NotificationService notifications)
    {
        InitializeComponent();
        _savedVehicles = savedVehicles;
        _pendingSearch = pendingSearch;
        _notifications = notifications;
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
        var expiryColor = (vehicle.IsExpired || vehicle.IsUrgent) ? Color.FromArgb("#FF3B30")
            : vehicle.IsExpiringSoon ? Color.FromArgb("#FF9500")
            : Color.FromArgb("#34C759");

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
            IsVisible = vehicle.SubTitle != vehicle.DisplayName
        };

        var expiryDot = new BoxView
        {
            WidthRequest = 10, HeightRequest = 10, CornerRadius = 5,
            BackgroundColor = expiryColor, VerticalOptions = LayoutOptions.Center
        };

        var expiryLabel = new Label
        {
            Text = vehicle.ExpiryDisplay,
            FontSize = 13,
            TextColor = expiryColor,
            FontAttributes = (vehicle.IsExpired || vehicle.IsExpiringSoon) ? FontAttributes.Bold : FontAttributes.None,
            VerticalOptions = LayoutOptions.Center
        };

        var expiryRow = new HorizontalStackLayout
        {
            Spacing = 6, Margin = new Thickness(0, 4, 0, 0),
            Children = { expiryDot, expiryLabel }
        };

        // Search / Remove buttons
        var searchBtn = new Button
        {
            Text = "Search",
            BackgroundColor = Color.FromArgb("#007AFF"), TextColor = Colors.White,
            FontSize = 15, FontAttributes = FontAttributes.Bold,
            CornerRadius = 10, HeightRequest = 40, HorizontalOptions = LayoutOptions.Fill
        };
        searchBtn.Clicked += (_, _) => OnSearchVehicle(vehicle);

        var removeBtn = new Button
        {
            Text = "Remove",
            BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#FF3B30"),
            BorderColor = Color.FromArgb("#FF3B30"), BorderWidth = 1.5,
            FontSize = 15, CornerRadius = 10, HeightRequest = 40, HorizontalOptions = LayoutOptions.Fill
        };
        removeBtn.Clicked += (_, _) => OnRemoveVehicle(vehicle);

        var buttonRow = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 8, Margin = new Thickness(0, 10, 0, 0)
        };
        buttonRow.SetColumn(searchBtn, 0);
        buttonRow.SetColumn(removeBtn, 1);
        buttonRow.Children.Add(searchBtn);
        buttonRow.Children.Add(removeBtn);

        var stack = new VerticalStackLayout
        {
            Padding = new Thickness(16), Spacing = 0,
            Children = { titleLabel, subLabel, expiryRow, buttonRow }
        };

        // Reminder section — always shown so users can always find and manage it
        stack.Children.Add(new BoxView
        {
            HeightRequest = 1,
            BackgroundColor = Color.FromArgb("#E5E5EA"),
            Margin = new Thickness(0, 12, 0, 0)
        });
        stack.Children.Add(BuildReminderSection(vehicle));

        // Prepend an urgent strip for vehicles expiring within 14 days or already expired
        View cardContent = stack;
        if (vehicle.IsUrgent || vehicle.IsExpired)
        {
            var days = vehicle.DaysUntilExpiry;
            string stripText;
            Color stripColor;

            if (vehicle.IsExpired)
            {
                stripText = $"MOT EXPIRED {-days} DAY{(-days == 1 ? "" : "S")} AGO";
                stripColor = Color.FromArgb("#FF3B30");
            }
            else
            {
                stripText = days switch
                {
                    0 => "MOT EXPIRES TODAY — BOOK NOW",
                    1 => "MOT EXPIRES TOMORROW — BOOK NOW",
                    _ => $"MOT EXPIRES IN {days} DAYS — BOOK NOW"
                };
                stripColor = days <= 7 ? Color.FromArgb("#FF3B30") : Color.FromArgb("#FF9500");
            }

            var strip = new Grid
            {
                BackgroundColor = stripColor,
                Padding = new Thickness(16, 10),
                Children =
                {
                    new Label
                    {
                        Text = stripText,
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
            BackgroundColor = Color.FromArgb("#FFFFFF"),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Shadow = new Shadow { Brush = Brush.Black, Offset = new Point(0, 2), Radius = 8, Opacity = 0.07f },
            Content = cardContent
        };

        return card;
    }

    private View BuildReminderSection(SavedVehicle vehicle)
    {
        if (!vehicle.MotExpiryDate.HasValue)
        {
            return new Label
            {
                Text = "No expiry date — search this vehicle to refresh its MOT data",
                FontSize = 13,
                TextColor = Color.FromArgb("#AEAEB2"),
                LineBreakMode = LineBreakMode.WordWrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
        }

        if (vehicle.IsExpired)
        {
            if (vehicle.ReminderEnabled)
                return BuildReminderActiveRow(vehicle, expired: true);

            return new Label
            {
                Text = "MOT expired — renew MOT and search again to set a reminder",
                FontSize = 13,
                TextColor = Color.FromArgb("#AEAEB2"),
                LineBreakMode = LineBreakMode.WordWrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
        }

        if (vehicle.ReminderEnabled)
            return BuildReminderActiveRow(vehicle, expired: false);

        var setBtn = new Button
        {
            Text = "Set MOT Reminder",
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#007AFF"),
            BorderColor = Color.FromArgb("#007AFF"),
            BorderWidth = 1.5,
            CornerRadius = 10,
            FontSize = 15,
            HeightRequest = 44,
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0, 10, 0, 0)
        };
        setBtn.Clicked += async (_, _) => await OnSetReminder(vehicle);
        return setBtn;
    }

    private View BuildReminderActiveRow(SavedVehicle vehicle, bool expired)
    {
        var reminderDate = vehicle.MotExpiryDate!.Value.AddDays(-30);
        if (reminderDate <= DateTime.Today) reminderDate = DateTime.Today.AddDays(1);

        var titleColor = expired ? Color.FromArgb("#AEAEB2") : Color.FromArgb("#007AFF");

        var infoStack = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center
        };
        infoStack.Children.Add(new Label
        {
            Text = expired ? "Reminder was set (MOT now expired)" : "MOT Reminder active",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = titleColor
        });
        if (!expired)
        {
            infoStack.Children.Add(new Label
            {
                Text = $"You'll be alerted on {reminderDate:d MMM yyyy}",
                FontSize = 12,
                TextColor = Color.FromArgb("#6E6E73")
            });
        }

        var turnOffBtn = new Button
        {
            Text = "Turn Off",
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#FF3B30"),
            BorderColor = Color.FromArgb("#FF3B30"),
            BorderWidth = 1.5,
            CornerRadius = 10,
            FontSize = 13,
            HeightRequest = 36,
            WidthRequest = 84,
            VerticalOptions = LayoutOptions.Center
        };
        turnOffBtn.Clicked += (_, _) => OnCancelReminder(vehicle);

        var row = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 10,
            Margin = new Thickness(0, 10, 0, 0)
        };
        row.SetColumn(infoStack, 0);
        row.SetColumn(turnOffBtn, 1);
        row.Children.Add(infoStack);
        row.Children.Add(turnOffBtn);

        return row;
    }

    private async Task OnSetReminder(SavedVehicle vehicle)
    {
        bool hasPermission = await _notifications.HasPermissionAsync();
        if (!hasPermission)
        {
            bool granted = await _notifications.RequestPermissionAsync();
            if (!granted)
            {
                await DisplayAlertAsync("Notifications Disabled",
                    "Please enable notifications for MOT History in iPhone Settings to use this feature.",
                    "OK");
                return;
            }
        }

        _notifications.ScheduleMotReminder(vehicle.Registration, vehicle.MotExpiryDate!.Value);
        vehicle.ReminderEnabled = true;
        _savedVehicles.Save(vehicle);
        RefreshList();
    }

    private void OnCancelReminder(SavedVehicle vehicle)
    {
        _notifications.CancelMotReminder(vehicle.Registration);
        vehicle.ReminderEnabled = false;
        _savedVehicles.Save(vehicle);
        RefreshList();
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

        if (vehicle.ReminderEnabled)
            _notifications.CancelMotReminder(vehicle.Registration);

        _savedVehicles.Remove(vehicle.Registration);
        RefreshList();
    }
}
