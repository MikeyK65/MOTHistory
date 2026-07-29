using Microsoft.Extensions.Logging;
using MOTHistory.Maui.Pages;
using MOTHistory.Maui.Services;

namespace MOTHistory.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<DVSASettingsService>();
        builder.Services.AddSingleton<RecentSearchesService>();
        builder.Services.AddSingleton<SavedVehiclesService>();
        builder.Services.AddSingleton<OfflineCacheService>();
        builder.Services.AddSingleton<PendingSearchService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddHttpClient<DVSAApiService>();
        builder.Services.AddHttpClient<DVLAApiService>();
        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<GaragePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
