using Core = ConnectFlyout.Core;

namespace ConnectFlyout.Presentation.Headsets;

/// <summary>
/// <see cref="IHeadset"/> backed by the native <see cref="Core.HeadsetClient"/>.
/// </summary>
public sealed class WinRtHeadset : IHeadset
{
    // One preset list shared by every headset, so a dropdown switching between headphones keeps
    // the same list (a new list clears its selection)
    private static readonly Lazy<IReadOnlyList<EqualizerPresetOption>> SharedPresets =
        new(() => [.. Core.HeadsetClient.GetEqualizerPresets().Select(preset => new EqualizerPresetOption(preset.Value, preset.Name))]);

    private readonly Core.HeadsetClient _client;

    public WinRtHeadset(string deviceName)
    {
        _client = new Core.HeadsetClient(deviceName);
        Features = ToFeatures(_client.Capabilities);

        _client.StateChanged += OnStateChanged;
        _client.Disconnected += OnDisconnected;
    }

    public event EventHandler<HeadsetSnapshot>? StateChanged;

    public event EventHandler? Disconnected;

    public string DeviceName => _client.DeviceName;

    public string ModelName => _client.ModelName;

    public bool IsKnownModel => _client.IsKnownModel;

    public HeadsetFeatures Features { get; }

    public HeadsetSnapshot State => WithPlaybackDevices(ToSnapshot(_client.State));

    public IReadOnlyList<EqualizerPresetOption> EqualizerPresets => SharedPresets.Value;

    public Task ConnectAsync(string bluetoothAddress) => _client.ConnectAsync(bluetoothAddress).AsTask();

    public void Disconnect() => _client.Disconnect();

    public Task RefreshBatteryAsync() => _client.RefreshBatteryAsync().AsTask();

    public Task SetNoiseControlAsync(NoiseControlSetting value) =>
        _client.SetNoiseControlAsync(new Core.NoiseControlInfo((Core.NoiseMode)value.Mode, value.AmbientLevel, value.FocusOnVoice)).AsTask();

    public Task SetEqualizerPresetAsync(int preset) => _client.SetEqualizerPresetAsync(preset).AsTask();

    public Task SetEqualizerCustomAsync(EqualizerSetting value) =>
        _client.SetEqualizerCustomAsync(new Core.EqualizerInfo(
            value.Preset,
            value.ClearBass,
            value.Bands[0],
            value.Bands[1],
            value.Bands[2],
            value.Bands[3],
            value.Bands[4])).AsTask();

    public Task SetConnectionQualityAsync(bool prioritizeStableConnection) =>
        _client.SetConnectionQualityAsync(prioritizeStableConnection).AsTask();

    public Task SetDseeAsync(bool enabled) => _client.SetDseeAsync(enabled).AsTask();

    public Task SetVptAsync(int preset) => _client.SetVptAsync(preset).AsTask();

    public Task SetSoundPositionAsync(int position) => _client.SetSoundPositionAsync(position).AsTask();

    public Task SetVoiceGuidanceAsync(int value) => _client.SetVoiceGuidanceAsync(value).AsTask();

    public Task PowerOffAsync() => _client.PowerOffAsync().AsTask();

    public Task SetSpeakToChatAsync(bool enabled) => _client.SetSpeakToChatAsync(enabled).AsTask();

    public Task SetAdaptiveVolumeAsync(bool enabled) => _client.SetAdaptiveVolumeAsync(enabled).AsTask();

    public Task SetAutoPowerOffAsync(int index) => _client.SetAutoPowerOffAsync(index).AsTask();

    public Task SwitchPlaybackAsync(string address) => _client.SwitchPlaybackAsync(address).AsTask();

    public void Dispose()
    {
        _client.StateChanged -= OnStateChanged;
        _client.Disconnected -= OnDisconnected;
        _client.Dispose();
    }

    internal static HeadsetSnapshot ToSnapshot(Core.HeadsetState state) => new(
        ToBattery(state.Battery),
        new NoiseControlSetting((NoiseMode)state.NoiseControl.Mode, state.NoiseControl.AmbientLevel, state.NoiseControl.FocusOnVoice),
        new EqualizerSetting(
            state.Equalizer.Preset,
            state.Equalizer.ClearBass,
            [state.Equalizer.Band1, state.Equalizer.Band2, state.Equalizer.Band3, state.Equalizer.Band4, state.Equalizer.Band5]),
        state.Dsee,
        state.SpeakToChat,
        state.AdaptiveVolume,
        state.AutoPowerOff,
        state.Firmware ?? "",
        state.Codec ?? "")
    {
        ConnectionQuality = state.ConnectionQuality,
        VoiceGuidance = state.VoiceGuidance,
        Vpt = state.Vpt,
        SoundPosition = state.SoundPosition,
    };

    private static int? Known(int level) => level < 0 ? null : level;

    // Earbuds: a side that isn't connected reports 0, and native "main" is just the lower side,
    // so it drops to 0 too. Treat 0 as "not connected" and leave main out for earbuds.
    private static BatteryLevels ToBattery(Core.BatteryInfo battery)
    {
        var earbuds = battery.Left >= 0 || battery.Right >= 0;
        return new BatteryLevels(
            earbuds ? null : Known(battery.Main),
            Earbud(battery.Left),
            Earbud(battery.Right),
            Known(battery.CaseBattery),
            battery.Charging);
    }

    private static int? Earbud(int level) => level > 0 ? level : null;

    private static HeadsetFeatures ToFeatures(Core.HeadsetCapabilities capabilities) => new(
        capabilities.DualBattery,
        capabilities.NoiseCancelling,
        capabilities.AmbientSound,
        capabilities.FocusOnVoice,
        capabilities.Equalizer,
        capabilities.ClearBass,
        capabilities.Dsee,
        capabilities.SpeakToChat,
        capabilities.AdaptiveVolume,
        capabilities.AutoPowerOff,
        capabilities.FirmwareInfo,
        capabilities.CodecInfo)
    {
        DseeExtreme = capabilities.DseeExtreme,
        ConnectionQuality = capabilities.ConnectionQuality,
        VoiceGuidance = capabilities.VoiceGuidance,
        Vpt = capabilities.Vpt,
        SoundPosition = capabilities.SoundPosition,
        PowerOff = capabilities.PowerOff,
        AutoPowerOffWhenRemoved = capabilities.AutoPowerOffWhenRemoved,
    };

    private void OnStateChanged(Core.HeadsetClient sender, Core.HeadsetState args) =>
        StateChanged?.Invoke(this, WithPlaybackDevices(ToSnapshot(args)));

    // The device list can't ride in the WinRT state struct, so it's read alongside it. An
    // empty WinRT array arrives as null.
    private HeadsetSnapshot WithPlaybackDevices(HeadsetSnapshot snapshot) => snapshot with
    {
        PlaybackDevices = [.. (_client.GetPlaybackDevices() ?? []).Select(device => new PlaybackDevice(device.Address, device.Name, device.Playing))],
    };

    private void OnDisconnected(Core.HeadsetClient sender, object args) => Disconnected?.Invoke(this, EventArgs.Empty);
}
