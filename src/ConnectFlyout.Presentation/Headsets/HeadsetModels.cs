using System.Collections.Immutable;

namespace ConnectFlyout.Presentation.Headsets;

/// <summary>
/// Noise control mode on the headset.
/// </summary>
public enum NoiseMode
{
    Off,
    NoiseCancelling,
    Ambient,
}

/// <summary>
/// State of the app's Bluetooth control link, separate from the Windows audio connection.
/// </summary>
public enum HeadsetConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}

/// <summary>
/// Battery levels in percent. Null means the headset didn't report that cell.
/// </summary>
public sealed record BatteryLevels(int? Main, int? Left, int? Right, int? Case, bool Charging)
{
    public static BatteryLevels Unknown { get; } = new(null, null, null, null, false);

    /// <summary>
    /// Lowest level across the headset's own cells (not the case). Null when none are known.
    /// </summary>
    public int? Lowest => new[] { Main, Left, Right }.Min();
}

/// <summary>
/// Noise control settings as the headset reports or receives them.
/// </summary>
public sealed record NoiseControlSetting(NoiseMode Mode, int AmbientLevel, bool FocusOnVoice);

/// <summary>
/// Equalizer preset plus the custom curve. Clear Bass and band values run from -10 to 10.
/// </summary>
public sealed record EqualizerSetting(int Preset, int ClearBass, ImmutableArray<int> Bands)
{
    /// <summary>
    /// Preset value the headset uses for a custom curve.
    /// </summary>
    public const int ManualPreset = 0xa0;
}

/// <summary>
/// Features the connected model supports.
/// </summary>
public sealed record HeadsetFeatures(
    bool DualBattery,
    bool NoiseCancelling,
    bool AmbientSound,
    bool FocusOnVoice,
    bool Equalizer,
    bool ClearBass,
    bool Dsee,
    bool SpeakToChat,
    bool AdaptiveVolume,
    bool AutoPowerOff,
    bool FirmwareInfo,
    bool CodecInfo)
{
    /// <summary>
    /// Uses the DSEE Extreme branding rather than regular DSEE.
    /// </summary>
    public bool DseeExtreme { get; init; }

    /// <summary>
    /// Supports changing the Bluetooth connection-quality mode.
    /// </summary>
    public bool ConnectionQuality { get; init; }

    /// <summary>
    /// Supports headset voice guidance.
    /// </summary>
    public bool VoiceGuidance { get; init; }

    /// <summary>
    /// Supports Virtualphones Technology surround presets.
    /// </summary>
    public bool Vpt { get; init; }

    /// <summary>
    /// Supports sound-position presets.
    /// </summary>
    public bool SoundPosition { get; init; }

    /// <summary>
    /// Supports the explicit power-off command.
    /// </summary>
    public bool PowerOff { get; init; }

    /// <summary>
    /// Auto power-off includes the When taken off option.
    /// </summary>
    public bool AutoPowerOffWhenRemoved { get; init; }
}

/// <summary>
/// Everything the headset last confirmed.
/// </summary>
public sealed record HeadsetSnapshot(
    BatteryLevels Battery,
    NoiseControlSetting NoiseControl,
    EqualizerSetting Equalizer,
    bool Dsee,
    bool SpeakToChat,
    bool AdaptiveVolume,
    int AutoPowerOff,
    string Firmware,
    string Codec)
{
    public int ConnectionQuality { get; init; } = -1;

    public int VoiceGuidance { get; init; } = -1;

    public int Vpt { get; init; } = -1;

    public int SoundPosition { get; init; } = -1;

    /// <summary>
    /// Devices connected to the headset (multipoint), when it can switch playback between
    /// them; empty when it can't.
    /// </summary>
    public IReadOnlyList<PlaybackDevice> PlaybackDevices { get; init; } = [];

    public static HeadsetSnapshot Empty { get; } = new(
        BatteryLevels.Unknown,
        new NoiseControlSetting(NoiseMode.Off, 0, false),
        new EqualizerSetting(0, 0, [0, 0, 0, 0, 0]),
        false,
        false,
        false,
        0,
        "",
        "");
}

/// <summary>
/// A device (PC, phone) connected to the headset, and whether it has the audio.
/// </summary>
public sealed record PlaybackDevice(string Address, string Name, bool Playing);

/// <summary>
/// One entry in the equalizer preset picker.
/// </summary>
public sealed record EqualizerPresetOption(int Value, string Name);
