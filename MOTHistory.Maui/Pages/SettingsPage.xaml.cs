using MOTHistory.Maui.Models;
using MOTHistory.Maui.Services;

namespace MOTHistory.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly DVSASettingsService _settingsService;

    public SettingsPage() : this(IPlatformApplication.Current!.Services.GetRequiredService<DVSASettingsService>()) { }

    public SettingsPage(DVSASettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadSettingsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"Could not load settings: {ex.Message}", "OK");
        }
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _settingsService.GetAsync();
        ApiKeyEntry.Text = settings.ApiKey;
        ClientIdEntry.Text = settings.ClientId;
        ClientSecretEntry.Text = settings.ClientSecret;
        BaseUrlEntry.Text = settings.BaseUrl;
        ScopeUrlEntry.Text = settings.ScopeUrl;
        TokenUrlEntry.Text = settings.TokenUrl;
        DvlaApiKeyEntry.Text = settings.DvlaApiKey;
        DvlaBaseUrlEntry.Text = settings.DvlaBaseUrl;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var settings = new DVSASettings
        {
            ApiKey = ApiKeyEntry.Text?.Trim(),
            ClientId = ClientIdEntry.Text?.Trim(),
            ClientSecret = ClientSecretEntry.Text?.Trim(),
            BaseUrl = BaseUrlEntry.Text?.Trim(),
            ScopeUrl = ScopeUrlEntry.Text?.Trim(),
            TokenUrl = TokenUrlEntry.Text?.Trim(),
            DvlaApiKey = DvlaApiKeyEntry.Text?.Trim(),
            DvlaBaseUrl = DvlaBaseUrlEntry.Text?.Trim(),
        };

        SetSavingState(true);

        try
        {
            // Run the save alongside a minimum delay so the spinner is always visible
            await Task.WhenAll(
                _settingsService.SaveAsync(settings),
                Task.Delay(600)
            );
            ShowStatus(success: true, title: "Settings saved", detail: null);
        }
        catch (Exception ex)
        {
            ShowStatus(success: false, title: "Save failed", detail: ex.Message);
        }
        finally
        {
            SetSavingState(false);
        }
    }

    private async void OnClearClicked(object? sender, EventArgs e)
    {
        bool confirmed = await DisplayAlertAsync(
            "Clear Credentials",
            "Remove all saved API credentials from this device?",
            "Clear", "Cancel");

        if (!confirmed) return;

        await _settingsService.SaveAsync(new DVSASettings());
        ApiKeyEntry.Text = "";
        ClientIdEntry.Text = "";
        ClientSecretEntry.Text = "";
        BaseUrlEntry.Text = "";
        ScopeUrlEntry.Text = "";
        TokenUrlEntry.Text = "";
        DvlaApiKeyEntry.Text = "";
        DvlaBaseUrlEntry.Text = "";
        ShowStatus(success: true, title: "Credentials cleared", detail: null);
    }

    private void SetSavingState(bool saving)
    {
        SaveButton.IsEnabled = !saving;
        SaveButton.Text = saving ? "Saving..." : "Save Settings";
        SavingLayout.IsVisible = saving;
        SaveIndicator.IsRunning = saving;
        if (saving) StatusCard.IsVisible = false;
    }

    private async void ShowStatus(bool success, string title, string? detail)
    {
        var bgColor = success ? Color.FromArgb("#E8F8EC") : Color.FromArgb("#FFF0EF");
        var textColor = success ? Color.FromArgb("#1A7A32") : Color.FromArgb("#CC1900");

        StatusCard.BackgroundColor = bgColor;
        StatusIcon.Text = success ? "✓" : "!";
        StatusIcon.TextColor = textColor;
        StatusTitle.Text = title;
        StatusTitle.TextColor = textColor;

        if (!string.IsNullOrWhiteSpace(detail))
        {
            StatusDetail.Text = detail;
            StatusDetail.TextColor = textColor;
            StatusDetail.IsVisible = true;
        }
        else
        {
            StatusDetail.IsVisible = false;
        }

        StatusCard.IsVisible = true;

        await Task.Delay(4000);
        StatusCard.IsVisible = false;
    }
}
