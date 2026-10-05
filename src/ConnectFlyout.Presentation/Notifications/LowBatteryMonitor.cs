using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.Notifications;

/// <summary>
/// Shows Windows notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows the low-battery notification. Clicking it opens the flyout on the headset with
    /// this ID.
    /// </summary>
    void ShowLowBattery(string headsetId, string deviceName, int level);
}

/// <summary>
/// Raises one low-battery notification each time a headset drops below
/// <see cref="Threshold"/> percent.
/// </summary>
/// <remarks>
/// The alert re-arms once the level is back at or above the threshold, or while charging.
/// </remarks>
public sealed class LowBatteryMonitor
{
    public const int Threshold = 20;

    private readonly AppSettings _settings;
    private readonly INotificationService _notifications;
    private readonly Dictionary<string, bool> _alerted = [];
    private readonly Lock _gate = new();

    public LowBatteryMonitor(AppSettings settings, INotificationService notifications)
    {
        _settings = settings;
        _notifications = notifications;
    }

    public void Update(string headsetId, string deviceName, BatteryLevels battery)
    {
        ArgumentNullException.ThrowIfNull(battery);

        var lowest = battery.Lowest;
        if (lowest is null)
        {
            return;
        }

        lock (_gate)
        {
            if (battery.Charging || lowest >= Threshold)
            {
                _alerted[headsetId] = false;
                return;
            }
            if (_alerted.GetValueOrDefault(headsetId) || !_settings.LowBatteryNotifications)
            {
                return;
            }
            _alerted[headsetId] = true;
        }
        _notifications.ShowLowBattery(headsetId, deviceName, lowest.Value);
    }
}
