using Foundation;
using UIKit;

namespace MOTHistory.Maui
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
        {
            // Catch unhandled managed exceptions before they reach native code
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            return base.FinishedLaunching(application, launchOptions);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var message = e.ExceptionObject?.ToString() ?? "Unknown error";
            System.Diagnostics.Debug.WriteLine($"[FATAL] UnhandledException: {message}");
            try
            {
                var path = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
                File.WriteAllText(path, $"{DateTime.UtcNow:O}\n{message}");
            }
            catch { }
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            System.Diagnostics.Debug.WriteLine($"[WARN] UnobservedTaskException: {e.Exception}");
        }
    }
}
