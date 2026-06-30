namespace MOTHistory.Maui.Services;

public class NotificationService
{
    private static string NotificationId(string registration) =>
        $"mot_reminder_{registration.Trim().ToUpperInvariant()}";

    /// <summary>
    /// Requests notification permission. Returns true if granted.
    /// On platforms other than iOS this always returns false.
    /// </summary>
    public async Task<bool> RequestPermissionAsync()
    {
#if IOS
        try
        {
            var (granted, _) = await global::UserNotifications.UNUserNotificationCenter.Current
                .RequestAuthorizationAsync(
                    global::UserNotifications.UNAuthorizationOptions.Alert |
                    global::UserNotifications.UNAuthorizationOptions.Sound |
                    global::UserNotifications.UNAuthorizationOptions.Badge);
            return granted;
        }
        catch
        {
            return false;
        }
#else
        return await Task.FromResult(false);
#endif
    }

    /// <summary>
    /// Checks whether the app currently has notification permission without prompting.
    /// </summary>
    public async Task<bool> HasPermissionAsync()
    {
#if IOS
        var settings = await global::UserNotifications.UNUserNotificationCenter.Current
            .GetNotificationSettingsAsync();
        return settings.AuthorizationStatus ==
               global::UserNotifications.UNAuthorizationStatus.Authorized;
#else
        return await Task.FromResult(false);
#endif
    }

    /// <summary>
    /// Schedules a local notification for 30 days before the given MOT expiry date at 9 AM.
    /// If the reminder date is already past, fires at 9 AM tomorrow instead.
    /// </summary>
    public void ScheduleMotReminder(string registration, DateTime expiryDate)
    {
#if IOS
        try
        {
            var reminderDate = expiryDate.Date.AddDays(-30).AddHours(9); // 9 AM, 30 days before
            if (reminderDate <= DateTime.Now)
                reminderDate = DateTime.Now.Date.AddDays(1).AddHours(9); // 9 AM tomorrow

            var content = new global::UserNotifications.UNMutableNotificationContent
            {
                Title = "MOT Due Soon",
                Body = $"{registration.ToUpperInvariant()} MOT expires on {expiryDate:d MMM yyyy}. Book your test now.",
                Sound = global::UserNotifications.UNNotificationSound.Default
            };

            var secondsUntil = (reminderDate - DateTime.Now).TotalSeconds;
            if (secondsUntil < 5) secondsUntil = 5;

            var trigger = global::UserNotifications.UNTimeIntervalNotificationTrigger
                .CreateTrigger(secondsUntil, repeats: false);

            var request = global::UserNotifications.UNNotificationRequest.FromIdentifier(
                NotificationId(registration), content, trigger);

            global::UserNotifications.UNUserNotificationCenter.Current
                .AddNotificationRequest(request, _ => { });
        }
        catch { }
#endif
    }

    /// <summary>
    /// Cancels any pending MOT reminder for the given registration.
    /// </summary>
    public void CancelMotReminder(string registration)
    {
#if IOS
        try
        {
            global::UserNotifications.UNUserNotificationCenter.Current
                .RemovePendingNotificationRequests([NotificationId(registration)]);
        }
        catch { }
#endif
    }
}
