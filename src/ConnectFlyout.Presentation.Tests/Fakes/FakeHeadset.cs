using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.Presentation.Tests.Fakes;

/// <summary>
/// Scriptable <see cref="IHeadset"/>. Commands succeed and update <see cref="State"/> unless
/// <see cref="FailNextCommandWith"/> is set; connects succeed unless <see cref="ConnectFailures"/>
/// still has entries.
/// </summary>
internal sealed class FakeHeadset : IHeadset
{
    private readonly Lock _gate = new();

    public FakeHeadset(string deviceName = "WF-1000XM6", HeadsetFeatures? features = null)
    {
        DeviceName = deviceName;
        Features = features ?? Xm6Features;
    }

    public static HeadsetFeatures Xm6Features { get; } = new(true, true, true, true, true, true, true, true, true, true, true, true)
    {
        DseeExtreme = true,
        PowerOff = true,
        AutoPowerOffWhenRemoved = true,
    };

    public static HeadsetFeatures Xm4Features { get; } = new(false, true, true, true, true, true, false, false, false, false, true, true)
    {
        PowerOff = true,
        AutoPowerOffWhenRemoved = true,
    };

    public static HeadsetFeatures Xb900nFeatures { get; } = new(false, true, true, true, true, true, true, false, false, true, true, true)
    {
        ConnectionQuality = true,
        VoiceGuidance = true,
        Vpt = true,
        SoundPosition = true,
        PowerOff = false,
        AutoPowerOffWhenRemoved = false,
    };

    public event EventHandler<HeadsetSnapshot>? StateChanged;

    public event EventHandler? Disconnected;

    public string DeviceName { get; }

    public string ModelName => DeviceName;

    public bool IsKnownModel => true;

    public HeadsetFeatures Features { get; }

    public HeadsetSnapshot State { get; set; } = HeadsetSnapshot.Empty with
    {
        Battery = new BatteryLevels(82, 85, 82, 95, false),
        NoiseControl = new NoiseControlSetting(NoiseMode.Ambient, 8, true),
        Codec = "LDAC",
    };

    public IReadOnlyList<EqualizerPresetOption> EqualizerPresets { get; } =
    [
        new EqualizerPresetOption(0x00, "Off"),
        new EqualizerPresetOption(0x10, "Bright"),
        new EqualizerPresetOption(0x16, "Bass Boost"),
        new EqualizerPresetOption(0xa0, "Manual"),
    ];

    /// <summary>
    /// Each connect attempt removes one entry and throws it. Empty means connects succeed.
    /// </summary>
    public Queue<Exception> ConnectFailures { get; } = new();

    public int ConnectAttempts { get; private set; }

    public int DisconnectCalls { get; private set; }

    public bool IsDisposed { get; private set; }

    public Exception? FailNextCommandWith { get; set; }

    public List<object> Commands { get; } = [];

    /// <summary>
    /// When set, the next connect attempt waits on this until the test completes it.
    /// </summary>
    public TaskCompletionSource? ConnectGate { get; set; }

    /// <summary>
    /// When true, commands stay in flight until <see cref="CompleteHeldCommands"/>.
    /// </summary>
    public bool HoldCommands { get; set; }

    private List<TaskCompletionSource> HeldCommands { get; } = [];

    public Task ConnectAsync(string bluetoothAddress)
    {
        lock (_gate)
        {
            ConnectAttempts++;
            if (ConnectGate is { } gate)
            {
                ConnectGate = null;
                return gate.Task;
            }
            if (ConnectFailures.TryDequeue(out var failure))
            {
                return Task.FromException(failure);
            }
        }
        return Task.CompletedTask;
    }

    public void CompleteHeldCommands()
    {
        List<TaskCompletionSource> held;
        lock (_gate)
        {
            held = [.. HeldCommands];
            HeldCommands.Clear();
        }
        foreach (var command in held)
        {
            command.SetResult();
        }
    }

    /// <summary>
    /// Raises StateChanged without changing <see cref="State"/>, like a headset echoing an
    /// older setting while a newer one is still in flight.
    /// </summary>
    public void RaiseEcho(HeadsetSnapshot snapshot) => StateChanged?.Invoke(this, snapshot);

    public void Disconnect() => DisconnectCalls++;

    public Task RefreshBatteryAsync() => Command("battery", state => state);

    public Task SetNoiseControlAsync(NoiseControlSetting value) => Command(value, state => state with { NoiseControl = value });

    public Task SetEqualizerPresetAsync(int preset) => Command(preset, state => state with { Equalizer = state.Equalizer with { Preset = preset } });

    public Task SetEqualizerCustomAsync(EqualizerSetting value) => Command(value, state => state with { Equalizer = value });

    public Task PowerOffAsync() => Command("power off", state => state);

    public Task SetConnectionQualityAsync(bool prioritizeStableConnection) =>
        Command(("connectionQuality", prioritizeStableConnection), state => state with { ConnectionQuality = prioritizeStableConnection ? 1 : 0 });

    public Task SetDseeAsync(bool enabled) => Command(("dsee", enabled), state => state with { Dsee = enabled });

    public Task SetVptAsync(int preset) =>
        Command(
            ("vpt", preset),
            state => state with
            {
                Vpt = preset,
                SoundPosition = preset != 0 ? 0 : state.SoundPosition,
            });

    public Task SetSoundPositionAsync(int position) =>
        Command(
            ("soundPosition", position),
            state => state with
            {
                SoundPosition = position,
                Vpt = position != 0 ? 0 : state.Vpt,
            });

    public Task SetVoiceGuidanceAsync(int value) =>
        Command(("voiceGuidance", value), state => state with { VoiceGuidance = value });

    public Task SetSpeakToChatAsync(bool enabled) => Command(("speakToChat", enabled), state => state with { SpeakToChat = enabled });

    public Task SetAdaptiveVolumeAsync(bool enabled) => Command(("adaptiveVolume", enabled), state => state with { AdaptiveVolume = enabled });

    public Task SetAutoPowerOffAsync(int index) => Command(("autoPowerOff", index), state => state with { AutoPowerOff = index });

    public Task SwitchPlaybackAsync(string address) => Command(("switchPlayback", address), state => state with
    {
        PlaybackDevices = [.. state.PlaybackDevices.Select(device => device with { Playing = device.Address == address })],
    });

    public void RaiseStateChanged(HeadsetSnapshot snapshot)
    {
        State = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }

    public void RaiseDisconnected() => Disconnected?.Invoke(this, EventArgs.Empty);

    public void Dispose() => IsDisposed = true;

    private Task Command(object command, Func<HeadsetSnapshot, HeadsetSnapshot> apply)
    {
        lock (_gate)
        {
            Commands.Add(command);
            if (FailNextCommandWith is { } failure)
            {
                FailNextCommandWith = null;
                return Task.FromException(failure);
            }
            State = apply(State);
            if (HoldCommands)
            {
                var held = new TaskCompletionSource();
                HeldCommands.Add(held);
                return held.Task;
            }
        }
        return Task.CompletedTask;
    }
}
