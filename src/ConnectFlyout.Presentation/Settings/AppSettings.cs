using System.Globalization;
using System.Text.Json;
using ConnectFlyout.Presentation.Scenes;
using ConnectFlyout.Presentation.Shortcuts;

namespace ConnectFlyout.Presentation.Settings;

/// <summary>
/// App theme choice.
/// </summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// What opening ConnectFlyout by hand does while its tray icon is hidden.
/// </summary>
public enum HiddenTrayLaunch
{
    OpenSettings,
    OpenFlyout,
    Nothing,
}

/// <summary>
/// Typed access to every app setting.
/// </summary>
public sealed class AppSettings
{
    private const string RememberedHeadsetKey         = "RememberedHeadset";
    private const string LowBatteryNotificationsKey   = "LowBatteryNotifications";
    private const string ThemeKey                     = "Theme";
    private const string DebugLoggingKey              = "DebugLogging";
    private const string ScenesKey                    = "Scenes";
    private const string HideTrayKey                  = "HideTray";
    private const string HideTrayWhileDisconnectedKey = "HideTrayWhileDisconnected";
    private const string HiddenTrayLaunchKey          = "HiddenTrayLaunch";
    private const string HideScenesKey                = "HideScenes";
    private const string HidePlaybackKey              = "HidePlayback";
    private const string HideSoundControlsKey         = "HideSoundControls";
    private const string HideAmbientControlsKey       = "HideAmbientControls";
    private const string AutoConnectKeyPrefix         = "AutoConnect.";
    private const string KnownHeadsetKeyPrefix        = "KnownHeadset.";
    private const string DisplayNameKeyPrefix         = "DisplayName.";
    private const string LastCaseBatteryKeyPrefix     = "LastCaseBattery.";
    private const string ShortcutKeyPrefix            = "Shortcut.";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ISettingsStore _store;

    public AppSettings(ISettingsStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Headset the flyout reopens on, by <see cref="Devices.ManagedHeadset.Id"/>. Null shows the picker.
    /// </summary>
    public string? RememberedHeadsetId
    {
        get => _store.GetString(RememberedHeadsetKey);
        set => _store.SetString(RememberedHeadsetKey, value);
    }

    public bool LowBatteryNotifications
    {
        get => GetBool(LowBatteryNotificationsKey, true);
        set => SetBool(LowBatteryNotificationsKey, value);
    }

    public AppTheme Theme
    {
        get => Enum.TryParse<AppTheme>(_store.GetString(ThemeKey), out var theme) ? theme : AppTheme.System;
        set => _store.SetString(ThemeKey, value.ToString());
    }

    public bool DebugLogging
    {
        get => GetBool(DebugLoggingKey, false);
        set => SetBool(DebugLoggingKey, value);
    }

    /// <summary>
    /// Keeps the tray icon hidden all the time. The app keeps running.
    /// </summary>
    public bool HideTray
    {
        get => GetBool(HideTrayKey, false);
        set => SetBool(HideTrayKey, value);
    }

    /// <summary>
    /// Keeps the tray icon hidden until Windows connects a pair of headphones. The app keeps
    /// running either way.
    /// </summary>
    public bool HideTrayWhileDisconnected
    {
        get => GetBool(HideTrayWhileDisconnectedKey, false);
        set => SetBool(HideTrayWhileDisconnectedKey, value);
    }

    /// <summary>
    /// What opening ConnectFlyout by hand does while the tray icon is hidden. An unknown saved
    /// value opens Settings.
    /// </summary>
    public HiddenTrayLaunch HiddenTrayLaunch
    {
        get => Enum.TryParse<HiddenTrayLaunch>(_store.GetString(HiddenTrayLaunchKey), out var action) && Enum.IsDefined(action)
            ? action
            : HiddenTrayLaunch.OpenSettings;
        set => _store.SetString(HiddenTrayLaunchKey, value.ToString());
    }

    /// <summary>
    /// Hides the Focus, Office and Aware scene tiles in the flyout.
    /// </summary>
    public bool HideScenes
    {
        get => GetBool(HideScenesKey, false);
        set => SetBool(HideScenesKey, value);
    }

    /// <summary>
    /// Hides the Playback section in the flyout (on headphones that can switch devices).
    /// </summary>
    public bool HidePlayback
    {
        get => GetBool(HidePlaybackKey, false);
        set => SetBool(HidePlaybackKey, value);
    }

    /// <summary>
    /// Hides the Ambient sound slider and Focus on voice in the flyout.
    /// </summary>
    public bool HideAmbientControls
    {
        get => GetBool(HideAmbientControlsKey, false);
        set => SetBool(HideAmbientControlsKey, value);
    }

    /// <summary>
    /// Hides the Equalizer and DSEE rows in the flyout.
    /// </summary>
    public bool HideSoundControls
    {
        get => GetBool(HideSoundControlsKey, false);
        set => SetBool(HideSoundControlsKey, value);
    }

    /// <summary>
    /// Whether the tray icon shows, given whether Windows has any headphones connected.
    /// </summary>
    public bool ShowsTrayIcon(bool headphonesConnected) => !HideTray && (!HideTrayWhileDisconnected || headphonesConnected);

    /// <summary>
    /// The combination for a global shortcut, like "Win+F10": the default until changed, "" once cleared.
    /// </summary>
    public string GetShortcut(ShortcutAction action) => _store.GetString(ShortcutKeyPrefix + action) ?? ShortcutActions.DefaultFor(action);

    public void SetShortcut(ShortcutAction action, string shortcut) => _store.SetString(ShortcutKeyPrefix + action, shortcut);

    /// <summary>
    /// Saved scenes, or <see cref="Scene.Defaults"/> when none are saved or the saved JSON is unreadable.
    /// </summary>
    public IReadOnlyList<Scene> Scenes
    {
        get
        {
            var json = _store.GetString(ScenesKey);
            if (string.IsNullOrEmpty(json))
            {
                return Scene.Defaults;
            }
            try
            {
                var scenes = JsonSerializer.Deserialize<List<Scene>>(json, JsonOptions);
                if (scenes is null)
                {
                    return Scene.Defaults;
                }
                return [.. scenes.Select(scene => scene.Glyph == Scene.OldOfficeGlyph ? scene with { Glyph = Scene.OfficeGlyph } : scene)];
            }
            catch (JsonException)
            {
                return Scene.Defaults;
            }
        }
        set => _store.SetString(ScenesKey, JsonSerializer.Serialize(value, JsonOptions));
    }

    /// <summary>
    /// Device name a headset had when it was first recognized as Sony (its model name), so it
    /// keeps working after it's renamed in Windows. Null for addresses never seen.
    /// </summary>
    public string? GetKnownHeadsetName(string headsetId) => _store.GetString(KnownHeadsetKeyPrefix + headsetId);

    public void SetKnownHeadsetName(string headsetId, string name) => _store.SetString(KnownHeadsetKeyPrefix + headsetId, name);

    /// <summary>
    /// The name entered in Settings > Devices, shown in the app instead of the name in Windows.
    /// Null when none is set.
    /// </summary>
    public string? GetDisplayName(string headsetId) => _store.GetString(DisplayNameKeyPrefix + headsetId);

    public void SetDisplayName(string headsetId, string? name) => _store.SetString(DisplayNameKeyPrefix + headsetId, name);

    /// <summary>
    /// Last charging case level the headset reported. The case only reports while an earbud
    /// sits in it, so this fills in the rest of the time.
    /// </summary>
    public int? GetLastCaseBattery(string headsetId) =>
        int.TryParse(_store.GetString(LastCaseBatteryKeyPrefix + headsetId), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ? level : null;

    public void SetLastCaseBattery(string headsetId, int level) =>
        _store.SetString(LastCaseBatteryKeyPrefix + headsetId, level.ToString(CultureInfo.InvariantCulture));

    public bool IsAutoConnectEnabled(string headsetId) => GetBool(AutoConnectKeyPrefix + headsetId, true);

    public void SetAutoConnectEnabled(string headsetId, bool enabled) => SetBool(AutoConnectKeyPrefix + headsetId, enabled);

    private bool GetBool(string key, bool fallback) =>
        bool.TryParse(_store.GetString(key), out var value) ? value : fallback;

    private void SetBool(string key, bool value) => _store.SetString(key, value.ToString(CultureInfo.InvariantCulture));
}
