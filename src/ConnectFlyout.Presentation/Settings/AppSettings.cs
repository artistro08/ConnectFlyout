using System.Globalization;
using System.Text.Json;
using ConnectFlyout.Presentation.Scenes;

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
/// Typed access to every app setting.
/// </summary>
public sealed class AppSettings
{
    private const string RememberedHeadsetKey = "RememberedHeadset";
    private const string LowBatteryNotificationsKey = "LowBatteryNotifications";
    private const string ThemeKey = "Theme";
    private const string DebugLoggingKey = "DebugLogging";
    private const string ScenesKey = "Scenes";
    private const string AutoConnectKeyPrefix = "AutoConnect.";
    private const string KnownHeadsetKeyPrefix = "KnownHeadset.";
    private const string LastCaseBatteryKeyPrefix = "LastCaseBattery.";

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
