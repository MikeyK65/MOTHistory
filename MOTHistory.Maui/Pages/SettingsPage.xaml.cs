using MOTHistory.Maui.Models;
using MOTHistory.Maui.Services;

namespace MOTHistory.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly DVSASettingsService _settingsService;

    // Parameterless ctor for Shell DataTemplate (Activator.CreateInstance path)
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
        };

        try
        {
            await _settingsService.SaveAsync(settings);
            ShowStatus("Settings saved.", isSuccess: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"Save failed: {ex.Message}", isSuccess: false);
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
        ShowStatus("Credentials cleared.", isSuccess: true);
    }

    private void ShowStatus(string message, bool isSuccess)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = isSuccess
            ? (Color)Application.Current!.Resources["PassColor"]
            : (Color)Application.Current!.Resources["FailColor"];
        StatusLabel.IsVisible = true;

        _ = Task.Delay(3000).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() => StatusLabel.IsVisible = false));
    }
}
