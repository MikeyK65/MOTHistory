namespace MOTHistory.Maui.Models;

public class SavedVehicle
{
    public string Registration { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime? MotExpiryDate { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    public string DisplayName => !string.IsNullOrWhiteSpace(Nickname) ? Nickname : Registration;

    public string SubTitle
    {
        get
        {
            var vehicle = $"{Make} {Model}".Trim();
            if (!string.IsNullOrWhiteSpace(Nickname) && !string.IsNullOrWhiteSpace(vehicle))
                return $"{Registration} · {vehicle}";
            if (!string.IsNullOrWhiteSpace(vehicle)) return vehicle;
            return Registration;
        }
    }

    public int DaysUntilExpiry =>
        MotExpiryDate.HasValue ? (int)(MotExpiryDate.Value.Date - DateTime.Today).TotalDays : int.MaxValue;

    public string ExpiryDisplay
    {
        get
        {
            if (!MotExpiryDate.HasValue) return "No expiry data";
            var days = DaysUntilExpiry;
            if (days < 0) return $"Expired {-days} day{(-days == 1 ? "" : "s")} ago";
            if (days == 0) return "Expires today";
            if (days == 1) return "Expires tomorrow";
            if (days <= 30) return $"Expires in {days} days";
            return $"Valid until {MotExpiryDate.Value:d MMM yyyy}";
        }
    }

    public bool ReminderEnabled { get; set; } = false;

    public bool IsExpired => MotExpiryDate.HasValue && DaysUntilExpiry < 0;
    public bool IsUrgent => !IsExpired && MotExpiryDate.HasValue && DaysUntilExpiry <= 14;
    public bool IsExpiringSoon => !IsExpired && !IsUrgent && MotExpiryDate.HasValue && DaysUntilExpiry <= 30;
    public bool IsValid => !IsExpired && !IsUrgent && !IsExpiringSoon;
    public bool CanSetReminder => MotExpiryDate.HasValue && !IsExpired;
}
