namespace ConnectFlyout.Presentation.Headsets;

/// <summary>
/// One Sony headset's control link.
/// </summary>
/// <remarks>
/// Every command completes once the headset acknowledges it, and <see cref="State"/> only
/// changes after that, so callers can revert their UI to <see cref="State"/> when a command fails.
/// Events are raised on a background thread.
/// </remarks>
public interface IHeadset : IDisposable
{
    string DeviceName { get; }

    string ModelName { get; }

    bool IsKnownModel { get; }

    HeadsetFeatures Features { get; }

    HeadsetSnapshot State { get; }

    IReadOnlyList<EqualizerPresetOption> EqualizerPresets { get; }

    event EventHandler<HeadsetSnapshot>? StateChanged;

    event EventHandler? Disconnected;

    Task ConnectAsync(string bluetoothAddress);

    void Disconnect();

    Task RefreshBatteryAsync();

    Task SetNoiseControlAsync(NoiseControlSetting value);

    Task SetEqualizerPresetAsync(int preset);

    Task SetEqualizerCustomAsync(EqualizerSetting value);

    /// <summary>
    /// Changes the Bluetooth connection-quality preference.
    /// </summary>
    Task SetConnectionQualityAsync(bool prioritizeStableConnection);

    Task SetDseeAsync(bool enabled);

    /// <summary>
    /// Changes the Virtualphones Technology surround preset.
    /// </summary>
    Task SetVptAsync(int preset);

    /// <summary>
    /// Changes the sound-position preset.
    /// </summary>
    Task SetSoundPositionAsync(int position);

    /// <summary>
    /// Enables or disables voice guidance.
    /// </summary>
    Task SetVoiceGuidanceAsync(int value);

    /// <summary>
    /// Turns the headset off. The control link drops afterwards.
    /// </summary>
    Task PowerOffAsync();

    Task SetSpeakToChatAsync(bool enabled);

    Task SetAdaptiveVolumeAsync(bool enabled);

    Task SetAutoPowerOffAsync(int index);

    /// <summary>
    /// Multipoint: moves playback to the connected device with this address. Fails when the
    /// headset refuses (on a call, say).
    /// </summary>
    Task SwitchPlaybackAsync(string address);
}
