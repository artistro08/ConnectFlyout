using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ConnectFlyout.Presentation.Common;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Notifications;
using ConnectFlyout.Presentation.Scenes;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.ViewModels;

internal sealed class AudioReconnectRequestedEventArgs(CancellationToken cancellationToken) : EventArgs
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationToken CancellationToken { get; } = cancellationToken;

    public Task<bool> Completion => _completion.Task;

    public void Complete(bool succeeded) => _completion.TrySetResult(succeeded);
}

/// <summary>
/// One headset's controls for the flyout device page and the settings window.
/// </summary>
/// <remarks>
/// Controls change right away when used, then send the command. When a command fails, every
/// control goes back to what the headset last confirmed and <see cref="ErrorMessage"/> shows
/// for five seconds. The ambient slider sends at most one command per 150 ms and always sends
/// where the drag stops. Headset notifications update the controls, except the noise controls
/// while slider commands are still queued.
/// </remarks>
public sealed class HeadsetViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan SliderInterval = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectionModeCodecTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectionModePollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How often the battery is re-read while one earbud is missing.
    /// </summary>
    public static readonly TimeSpan MissingEarbudPollInterval = TimeSpan.FromSeconds(5);

    private const string NoValue = "—";

    private readonly ManagedHeadset _managed;
    private readonly IHeadset _headset;
    private readonly AppSettings _settings;
    private readonly LowBatteryMonitor _lowBattery;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly UiContext _ui = new();
    private readonly Throttler _noiseThrottler;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private HeadsetSnapshot _snapshot = HeadsetSnapshot.Empty;
    private HeadsetConnectionState _connectionState;
    private NoiseMode _noiseMode;
    private double _ambientLevel = 10;
    private bool _focusOnVoice;
    private int _selectedEqualizerIndex = -1;
    private int _dseeIndex;
    private int _connectionQualityIndex = -1;
    private bool _connectionQualityBusy;
    private bool _voiceGuidance;
    private int _vptPresetIndex = -1;
    private int _soundPositionIndex = -1;
    private bool _spatialCommandInFlight;
    private bool _speakToChat;
    private bool _adaptiveVolume;
    private int _autoPowerOffIndex;
    private double _clearBass;
    private double _band1;
    private double _band2;
    private double _band3;
    private double _band4;
    private double _band5;
    private string? _errorMessage;
    private ITimer? _errorTimer;
    private ITimer? _missingEarbudTimer;
    private ITimer? _connectTimer;
    private bool _bluetoothConnecting;
    private bool _connectReachedWindows;
    private bool _connectRetried;
    private bool _applying;
    private int _noiseCommandsInFlight;

    public HeadsetViewModel(
        ManagedHeadset managed,
        AppSettings settings,
        LowBatteryMonitor lowBattery,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(managed);

        _managed = managed;
        _headset = managed.Headset;
        _settings = settings;
        _lowBattery = lowBattery;
        _timeProvider = timeProvider;
        _logger = logger;
        _noiseThrottler = new Throttler(SliderInterval, timeProvider);
        _connectionState = managed.ConnectionState;

        SetNoiseModeCommand = new RelayCommand<string>(mode =>
        {
            if (Enum.TryParse<NoiseMode>(mode, out var parsed))
            {
                SelectNoiseMode(parsed);
            }
        });
        ApplySceneCommand = new RelayCommand<Scene>(scene =>
        {
            if (scene is not null)
            {
                ApplyScene(scene);
            }
        });
        ApplyCustomEqualizerCommand = new AsyncRelayCommand(ApplyCustomEqualizerAsync);
        PowerOffCommand = new AsyncRelayCommand(() => RunCommandAsync(_headset.PowerOffAsync, "power"));
        SwitchPlaybackCommand = new RelayCommand<PlaybackDevice>(device =>
        {
            if (device is not null)
            {
                SwitchPlayback(device);
            }
        });
        DisconnectCommand = new RelayCommand(() => AutoConnect = false);
        ReconnectCommand = new RelayCommand(() => ReconnectRequested?.Invoke(this, EventArgs.Empty));
        ConnectCommand = new RelayCommand(() => ConnectRequested?.Invoke(this, EventArgs.Empty));

        _headset.StateChanged += OnHeadsetStateChanged;
        SceneItems = [.. settings.Scenes.Select(scene => new SceneItemViewModel(scene))];
        ApplySnapshot(_headset.State);
    }

    // =========================================================================
    // IDENTITY
    // =========================================================================

    public string Id => _managed.Id;

    public string DeviceName => _managed.Name;

    public string ModelName => _headset.ModelName;

    public bool IsKnownModel => _headset.IsKnownModel;

    public HeadsetFeatures Features => _headset.Features;

    public string Firmware => string.IsNullOrEmpty(_snapshot.Firmware) ? NoValue : _snapshot.Firmware;

    public string Codec => string.IsNullOrEmpty(_snapshot.Codec) ? NoValue : _snapshot.Codec;

    // =========================================================================
    // CONNECTION
    // =========================================================================

    public HeadsetConnectionState ConnectionState => _connectionState;

    public bool IsConnected => _connectionState == HeadsetConnectionState.Connected;

    public bool IsConnecting => _connectionState == HeadsetConnectionState.Connecting;

    /// <summary>
    /// Whether Windows has the headset connected (audio), apart from the app's control link.
    /// </summary>
    public bool IsWindowsConnected => _managed.IsWindowsConnected;

    /// <summary>
    /// The app let go of the headset (Disconnect) and stays off it until Reconnect.
    /// </summary>
    public bool IsReleased => !AutoConnect;

    /// <summary>
    /// Worth showing as a destination: Windows has it and the app isn't holding off.
    /// </summary>
    public bool IsAvailable => IsWindowsConnected && !IsReleased;

    /// <summary>
    /// The warning bar and disabled controls show while there's no control link and none is
    /// on the way.
    /// </summary>
    public bool ShowDisconnectedWarning => _connectionState == HeadsetConnectionState.Disconnected;

    /// <summary>
    /// Reconnect takes the Disconnect button's place while disconnected, but only while Windows
    /// has the headset; without that there's nothing to reconnect to.
    /// </summary>
    public bool ShowReconnect => _connectionState == HeadsetConnectionState.Disconnected && IsWindowsConnected;

    /// <summary>
    /// Connect (Bluetooth) shows while the headset is paired but Windows isn't connected to it,
    /// and not while a connect is already under way.
    /// </summary>
    public bool ShowConnect => !IsWindowsConnected && !IsBluetoothConnecting;

    /// <summary>
    /// Windows has been asked to connect the headset and hasn't yet.
    /// </summary>
    public bool IsBluetoothConnecting
    {
        get => _bluetoothConnecting;
        private set
        {
            if (!SetProperty(ref _bluetoothConnecting, value))
            {
                return;
            }
            OnPropertyChanged(nameof(ShowConnect));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(PickerStatusText));
        }
    }

    /// <summary>
    /// How long a Bluetooth connect gets before it counts as failed.
    /// </summary>
    public static TimeSpan BluetoothConnectTimeout { get; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Raised by <see cref="ConnectCommand"/>; the flyout asks Windows to connect the headset.
    /// </summary>
    public event EventHandler? ConnectRequested;

    /// <summary>
    /// Connects the headset over Bluetooth, from Windows' side (like Connect in Settings).
    /// </summary>
    public IRelayCommand ConnectCommand { get; }

    public string StatusText => _connectionState switch
    {
        _ when IsBluetoothConnecting => "Connecting…",
        HeadsetConnectionState.Connected when !string.IsNullOrEmpty(_snapshot.Codec) => $"Connected · {_snapshot.Codec}",
        HeadsetConnectionState.Connected when !IsKnownModel => "Connected · Unverified model",
        HeadsetConnectionState.Connected => "Connected",
        HeadsetConnectionState.Connecting => "Connecting…",
        // Windows still has them but the app doesn't (let go, or the control link is down)
        _ when IsWindowsConnected => "App Disconnected",
        _ => "Disconnected",
    };

    /// <summary>
    /// The settings pages' warning bar: the app's side or the whole device.
    /// </summary>
    public string DisconnectedMessage => IsWindowsConnected ? "App Disconnected" : "Device is disconnected.";

    /// <summary>
    /// The status line only shows while not connected; once connected the header shows the
    /// battery and <see cref="AudioText"/> instead, like Sony's app.
    /// </summary>
    public bool ShowStatus => !IsConnected;

    /// <summary>
    /// DSEE branding advertised by the headset's feature profile.
    /// </summary>
    public string DseeName => Features.DseeExtreme ? "DSEE Extreme" : "DSEE";

    /// <summary>
    /// Codec plus the configured DSEE setting.
    /// </summary>
    public string AudioText => string.Join(" · ", new[]
    {
        _snapshot.Codec,
        _dseeIndex == 1 ? DseeName : "",
    }.Where(part => part.Length > 0));

    public bool AutoConnect
    {
        get => _settings.IsAutoConnectEnabled(Id);
        set
        {
            if (value == AutoConnect)
            {
                return;
            }
            _settings.SetAutoConnectEnabled(Id, value);
            RaiseAutoConnectChanged();
            AutoConnectChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? AutoConnectChanged;

    /// <summary>
    /// Raised by <see cref="ReconnectCommand"/>; the flyout asks the headset manager to reconnect.
    /// </summary>
    public event EventHandler? ReconnectRequested;

    /// <summary>
    /// Requests a full Windows Bluetooth reconnect for AAC mode and completes when Windows
    /// accepts or rejects the reconnect request.
    /// </summary>
    internal event EventHandler<AudioReconnectRequestedEventArgs>? AudioReconnectRequested;

    /// <summary>
    /// Lets go of the headset until Reconnect (see <see cref="HeadsetManager.Release"/>).
    /// </summary>
    public IRelayCommand DisconnectCommand { get; }

    /// <summary>
    /// Connects again, undoing Disconnect.
    /// </summary>
    public IRelayCommand ReconnectCommand { get; }

    // =========================================================================
    // BATTERY
    // =========================================================================

    /// <summary>
    /// The battery row hides while Windows doesn't have the headset; the levels would only be stale.
    /// </summary>
    public bool ShowBattery => IsWindowsConnected;

    public bool ShowDualBattery => Features.DualBattery && (ShowLeftBattery || ShowRightBattery);

    /// <summary>
    /// False while that earbud isn't connected (no level reported), so its badge hides.
    /// </summary>
    public bool ShowLeftBattery => _snapshot.Battery.Left is not null;

    public bool ShowRightBattery => _snapshot.Battery.Right is not null;

    public bool ShowSingleBattery => !ShowDualBattery;

    public string LeftBatteryText => Percent(_snapshot.Battery.Left);

    public string RightBatteryText => Percent(_snapshot.Battery.Right);

    public string CaseBatteryText => Percent(_snapshot.Battery.Case);

    public bool ShowCaseBattery => _snapshot.Battery.Case is not null;

    public string MainBatteryText => Percent(_snapshot.Battery.Main);

    public string MainBatteryGlyph => Glyphs.Battery(_snapshot.Battery.Main);

    // =========================================================================
    // NOISE CONTROL
    // =========================================================================

    public IRelayCommand<string> SetNoiseModeCommand { get; }

    public IRelayCommand<Scene> ApplySceneCommand { get; }

    public IReadOnlyList<SceneItemViewModel> SceneItems { get; private set; } = [];

    public NoiseMode NoiseMode => _noiseMode;

    public bool IsNoiseOff => _noiseMode == NoiseMode.Off;

    public bool IsNoiseCancelling => _noiseMode == NoiseMode.NoiseCancelling;

    public bool IsAmbient => _noiseMode == NoiseMode.Ambient;

    public double AmbientLevel
    {
        get => _ambientLevel;
        set
        {
            var level = Math.Clamp(Math.Round(value), 1, 20);
            if (!SetProperty(ref _ambientLevel, level) || _applying)
            {
                return;
            }
            OnPropertyChanged(nameof(AmbientLevelText));
            UpdateActiveScene();
            _noiseThrottler.Run(SendNoiseControlAsync);
        }
    }

    public string AmbientLevelText => $"{_ambientLevel:0}";

    public bool FocusOnVoice
    {
        get => _focusOnVoice;
        set
        {
            if (!SetProperty(ref _focusOnVoice, value) || _applying)
            {
                return;
            }
            UpdateActiveScene();
            _ = SendNoiseControlAsync();
        }
    }

    // =========================================================================
    // SOUND
    // =========================================================================

    public IReadOnlyList<EqualizerPresetOption> EqualizerPresets => _headset.EqualizerPresets;

    /// <summary>
    /// Index into <see cref="EqualizerPresets"/>. A -1 from the UI is ignored: a ComboBox
    /// writes it back when its preset list is swapped (switching headphones), which would
    /// otherwise leave the dropdown blank.
    /// </summary>
    public int SelectedEqualizerIndex
    {
        get => _selectedEqualizerIndex;
        set
        {
            if (value < 0 && !_applying)
            {
                // Tell the control again, once it has finished swapping lists
                if (_selectedEqualizerIndex >= 0)
                {
                    _ui.Defer(() => OnPropertyChanged(nameof(SelectedEqualizerIndex)));
                }
                return;
            }
            if (!_applying &&
                (value >= EqualizerPresets.Count ||
                 (HasConnectionQualityControl && !CanEditHeadsetSound)))
            {
                _ui.Defer(() => OnPropertyChanged(nameof(SelectedEqualizerIndex)));
                return;
            }
            if (!SetProperty(ref _selectedEqualizerIndex, value) || _applying)
            {
                return;
            }
            var preset = EqualizerPresets[value].Value;
            _ = RunCommandAsync(() => _headset.SetEqualizerPresetAsync(preset), "equalizer");
        }
    }

    /// <summary>
    /// 0 is Off, 1 is Auto.
    /// </summary>
    public int DseeIndex
    {
        get => _dseeIndex;
        set
        {
            if (!_applying && HasConnectionQualityControl && !CanEditHeadsetSound)
            {
                _ui.Defer(() => OnPropertyChanged(nameof(DseeIndex)));
                return;
            }
            if (!SetProperty(ref _dseeIndex, value))
            {
                return;
            }
            OnPropertyChanged(nameof(AudioText));
            if (_applying)
            {
                return;
            }
            _ = RunCommandAsync(() => _headset.SetDseeAsync(value == 1), "DSEE");
        }
    }

    public static IReadOnlyList<string> DseeOptions { get; } = ["Off", "Auto"];

    /// <summary>
    /// Whether this headset exposes the Bluetooth connection-quality control.
    /// </summary>
    public bool HasConnectionQualityControl => Features.ConnectionQuality;

    /// <summary>
    /// Connection-mode choices shown for headsets that support connection-quality control.
    /// </summary>
    public static IReadOnlyList<string> ConnectionQualityOptions { get; } =
        ["SBC", "AAC", "High Quality"];

    private static int ConnectionModeIndexFor(HeadsetSnapshot snapshot)
    {
        if (snapshot.Codec == "SBC")
        {
            return 0;
        }

        if (snapshot.Codec == "AAC")
        {
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Codec))
        {
            return 2;
        }

        return snapshot.ConnectionQuality switch
        {
            1 => 0,
            0 => 2,
            _ => -1,
        };
    }

    /// <summary>
    /// Selected connection mode: 0 is Stability/SBC, 1 is AAC, and 2 is High Quality.
    /// </summary>
    public int ConnectionQualityIndex
    {
        get => _connectionQualityIndex;
        set
        {
            if (_applying)
            {
                if (SetProperty(ref _connectionQualityIndex, value))
                {
                    OnPropertyChanged(nameof(CanEditHeadsetSound));
                    OnPropertyChanged(nameof(CanChangeSpatialEffect));
                }
                return;
            }

            if (!HasConnectionQualityControl ||
                !IsConnected ||
                _connectionQualityBusy ||
                value is < 0 or > 2)
            {
                _ui.Defer(() => OnPropertyChanged(nameof(ConnectionQualityIndex)));
                return;
            }

            if (!SetProperty(ref _connectionQualityIndex, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanEditHeadsetSound));
            OnPropertyChanged(nameof(CanChangeSpatialEffect));
            _ = ApplyConnectionQualityAsync(value);
        }
    }

    private async Task ApplyConnectionQualityAsync(int requestedIndex)
    {
        var cancellationToken = _lifetimeCancellation.Token;
        _connectionQualityBusy = true;
        _ui.Post(() =>
        {
            OnPropertyChanged(nameof(CanChangeConnectionMode));
            OnPropertyChanged(nameof(CanEditHeadsetSound));
            OnPropertyChanged(nameof(CanChangeSpatialEffect));
        });

        try
        {
            var highQualityFromAac =
                requestedIndex == 2 &&
                string.Equals(_snapshot.Codec, "AAC", StringComparison.Ordinal);

            // aptX-family High Quality does not run DSEE on WH-XB900N. Turn DSEE off
            // while the headset is still on a codec where that command is meaningful.
            if (requestedIndex == 2 && DseeIndex == 1)
            {
                await _headset.SetDseeAsync(false).ConfigureAwait(false);

                _ui.Post(() =>
                {
                    _applying = true;
                    try
                    {
                        DseeIndex = 0;
                    }
                    finally
                    {
                        _applying = false;
                    }
                });
            }

            // AAC and High Quality both use Sony's Quality policy. Re-sending Quality
            // while AAC is active does not renegotiate the codec, so pass through
            // Stability and wait for the headset to report SBC before returning to Quality.
            if (highQualityFromAac)
            {
                await _headset.SetConnectionQualityAsync(true).ConfigureAwait(false);

                await WaitForCodecAsync("SBC", cancellationToken).ConfigureAwait(false);
            }

            // SBC -> Sony Stability.
            // AAC / High Quality -> Sony Quality.
            await _headset.SetConnectionQualityAsync(requestedIndex == 0).ConfigureAwait(false);

            if (requestedIndex == 1)
            {
                var request = new AudioReconnectRequestedEventArgs(cancellationToken);
                if (AudioReconnectRequested is null)
                {
                    throw new InvalidOperationException("No Windows Bluetooth reconnect handler is available.");
                }

                _ui.Post(() =>
                {
                    var reconnectRequested = AudioReconnectRequested;
                    if (reconnectRequested is null)
                    {
                        request.Complete(false);
                        return;
                    }
                    reconnectRequested.Invoke(this, request);
                });
                if (!await request.Completion.WaitAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Windows did not complete the Bluetooth reconnect request.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogMessages.CommandFailed(_logger, ex, "Bluetooth connection mode", DeviceName);
            _ui.Post(() =>
            {
                ApplySnapshot(_headset.State);
                ShowError(HeadsetErrorMessages.Describe(ex));
            });
        }
        finally
        {
            _connectionQualityBusy = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                _ui.Post(() =>
                {
                    OnPropertyChanged(nameof(CanChangeConnectionMode));
                    OnPropertyChanged(nameof(CanEditHeadsetSound));
                    OnPropertyChanged(nameof(CanChangeSpatialEffect));
                });
            }
        }
    }

    private async Task WaitForCodecAsync(string codec, CancellationToken cancellationToken)
    {
        var deadline = _timeProvider.GetUtcNow() + ConnectionModeCodecTimeout;
        while (!string.Equals(_snapshot.Codec, codec, StringComparison.Ordinal))
        {
            var remaining = deadline - _timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException($"The headphones did not report {codec} while switching connection mode.");
            }

            await Task.Delay(
                remaining < ConnectionModePollInterval ? remaining : ConnectionModePollInterval,
                _timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether this headset exposes VPT or sound-position controls.
    /// </summary>
    public bool HasLegacySpatialControls => Features.Vpt || Features.SoundPosition;

    /// <summary>
    /// Whether the connection-mode picker can accept a new selection.
    /// </summary>
    public bool CanChangeConnectionMode =>
        HasConnectionQualityControl && IsConnected && !_connectionQualityBusy;

    /// <summary>
    /// Whether sound settings are valid for the headset's current connection mode.
    /// </summary>
    public bool CanEditHeadsetSound =>
        IsConnected &&
        (!HasConnectionQualityControl ||
         (!_connectionQualityBusy && _snapshot.Codec is "SBC" or "AAC"));

    /// <summary>
    /// Whether the legacy spatial pickers can accept a new selection.
    /// </summary>
    public bool CanChangeSpatialEffect => CanEditHeadsetSound && !_spatialCommandInFlight;

    /// <summary>
    /// Whether the compact popup should show the equalizer control.
    /// </summary>
    public bool ShowPopupEqualizer => Features.Equalizer;

    /// <summary>
    /// Whether the compact popup should show the DSEE control.
    /// </summary>
    public bool ShowPopupDsee => Features.Dsee;

    /// <summary>
    /// Whether the compact popup should show its sound-controls section.
    /// </summary>
    /// <remarks>
    /// Connection-quality headsets hide these controls after reporting a codec other than
    /// SBC or AAC.
    /// </remarks>
    public bool ShowPopupSoundSection =>
        !HasConnectionQualityControl ||
        ((ShowPopupEqualizer || ShowPopupDsee) &&
         (string.IsNullOrEmpty(_snapshot.Codec) ||
          _snapshot.Codec is "SBC" or "AAC"));

    /// <summary>
    /// Whether the compact popup should separate connection mode from sound controls.
    /// </summary>
    public bool ShowPopupConnectionSeparator =>
        HasConnectionQualityControl && ShowPopupSoundSection;

    /// <summary>
    /// VPT surround presets exposed by the legacy spatial control.
    /// </summary>
    public static IReadOnlyList<string> VptPresetOptions { get; } =
        ["Off", "Outdoor Stage", "Arena", "Concert Hall", "Club"];

    /// <summary>
    /// Sound-position choices exposed by the legacy spatial control.
    /// </summary>
    public static IReadOnlyList<string> SoundPositionOptions { get; } =
        ["Normal", "Left", "Right", "Front", "Back Left", "Back Right"];

    private static readonly int[] SoundPositionCodes = [0x00, 0x01, 0x02, 0x03, 0x11, 0x12];

    /// <summary>
    /// Selected VPT surround preset index.
    /// </summary>
    public int VptPresetIndex
    {
        get => _vptPresetIndex;
        set
        {
            if (_applying)
            {
                SetProperty(ref _vptPresetIndex, value);
                return;
            }

            if (!Features.Vpt ||
                !CanEditHeadsetSound ||
                _spatialCommandInFlight ||
                value is < 0 or > 4)
            {
                _ui.Defer(() => OnPropertyChanged(nameof(VptPresetIndex)));
                return;
            }

            if (SetProperty(ref _vptPresetIndex, value))
            {
                _ = ApplySpatialAsync(() => _headset.SetVptAsync(value));
            }
        }
    }

    /// <summary>
    /// Selected sound-position option index.
    /// </summary>
    public int SoundPositionIndex
    {
        get => _soundPositionIndex;
        set
        {
            if (_applying)
            {
                SetProperty(ref _soundPositionIndex, value);
                return;
            }

            if (!Features.SoundPosition ||
                !CanEditHeadsetSound ||
                _spatialCommandInFlight ||
                value is < 0 or > 5)
            {
                _ui.Defer(() => OnPropertyChanged(nameof(SoundPositionIndex)));
                return;
            }

            if (SetProperty(ref _soundPositionIndex, value))
            {
                _ = ApplySpatialAsync(() => _headset.SetSoundPositionAsync(SoundPositionCodes[value]));
            }
        }
    }

    private async Task ApplySpatialAsync(Func<Task> apply)
    {
        _spatialCommandInFlight = true;
        _ui.Post(() => OnPropertyChanged(nameof(CanChangeSpatialEffect)));

        Exception? failure = null;
        try
        {
            await apply().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
            LogMessages.CommandFailed(_logger, ex, "sound effect", DeviceName);
        }
        finally
        {
            _ui.Post(() =>
            {
                _spatialCommandInFlight = false;
                ApplySnapshot(_headset.State);
                OnPropertyChanged(nameof(CanChangeSpatialEffect));
                if (failure is not null)
                {
                    ShowError(HeadsetErrorMessages.Describe(failure));
                }
            });
        }
    }

    /// <summary>
    /// Whether this headset exposes a voice-guidance toggle.
    /// </summary>
    public bool HasVoiceGuidanceControl => Features.VoiceGuidance;

    /// <summary>
    /// Whether headset voice guidance is enabled.
    /// </summary>
    public bool VoiceGuidance
    {
        get => _voiceGuidance;
        set
        {
            if (_applying)
            {
                SetProperty(ref _voiceGuidance, value);
                return;
            }

            if (!HasVoiceGuidanceControl || !IsConnected)
            {
                _ui.Defer(() => OnPropertyChanged(nameof(VoiceGuidance)));
                return;
            }

            if (SetProperty(ref _voiceGuidance, value))
            {
                _ = RunCommandAsync(() => _headset.SetVoiceGuidanceAsync(value ? 1 : 0), "voice guidance");
            }
        }
    }

    /// <summary>
    /// Whether the compact popup should offer the power-off action.
    /// </summary>
    public bool CanPowerOffFromPopup => IsConnected && Features.PowerOff;

    public double ClearBass
    {
        get => _clearBass;
        set => SetProperty(ref _clearBass, Math.Clamp(Math.Round(value), -10, 10));
    }

    public double Band1
    {
        get => _band1;
        set => SetProperty(ref _band1, Math.Clamp(Math.Round(value), -10, 10));
    }

    public double Band2
    {
        get => _band2;
        set => SetProperty(ref _band2, Math.Clamp(Math.Round(value), -10, 10));
    }

    public double Band3
    {
        get => _band3;
        set => SetProperty(ref _band3, Math.Clamp(Math.Round(value), -10, 10));
    }

    public double Band4
    {
        get => _band4;
        set => SetProperty(ref _band4, Math.Clamp(Math.Round(value), -10, 10));
    }

    public double Band5
    {
        get => _band5;
        set => SetProperty(ref _band5, Math.Clamp(Math.Round(value), -10, 10));
    }

    public IAsyncRelayCommand ApplyCustomEqualizerCommand { get; }

    /// <summary>
    /// Turns the headset off (the footer's "Turn off" button).
    /// </summary>
    public IAsyncRelayCommand PowerOffCommand { get; }

    // =========================================================================
    // PLAYBACK
    // =========================================================================

    /// <summary>
    /// Devices connected to the headset (multipoint), for moving the audio between them.
    /// Empty when the headset can't switch.
    /// </summary>
    public IReadOnlyList<PlaybackDevice> PlaybackDevices { get; private set; } = [];

    /// <summary>
    /// The Playback section shows while connected with at least two devices to choose from.
    /// </summary>
    public bool ShowPlayback => IsConnected && PlaybackDevices.Count >= 2;

    /// <summary>
    /// Moves the audio to the given device; the one already playing is left alone.
    /// </summary>
    public IRelayCommand<PlaybackDevice> SwitchPlaybackCommand { get; }

    // =========================================================================
    // SYSTEM
    // =========================================================================

    public bool SpeakToChat
    {
        get => _speakToChat;
        set
        {
            if (!SetProperty(ref _speakToChat, value) || _applying)
            {
                return;
            }
            _ = RunCommandAsync(() => _headset.SetSpeakToChatAsync(value), "Speak-to-Chat");
        }
    }

    public bool AdaptiveVolume
    {
        get => _adaptiveVolume;
        set
        {
            if (!SetProperty(ref _adaptiveVolume, value) || _applying)
            {
                return;
            }
            _ = RunCommandAsync(() => _headset.SetAdaptiveVolumeAsync(value), "adaptive volume");
        }
    }

    public int AutoPowerOffIndex
    {
        get => _autoPowerOffIndex;
        set
        {
            if (!SetProperty(ref _autoPowerOffIndex, value) || _applying || value < 0)
            {
                return;
            }
            _ = RunCommandAsync(() => _headset.SetAutoPowerOffAsync(value), "auto power-off");
        }
    }

    public static IReadOnlyList<string> AutoPowerOffOptions { get; } =
        ["Off", "After 5 minutes", "After 30 minutes", "After 1 hour", "After 3 hours", "When taken off"];

    private static IReadOnlyList<string> TimedAutoPowerOffOptions { get; } =
        ["Off", "After 5 minutes", "After 30 minutes", "After 1 hour", "After 3 hours"];

    /// <summary>
    /// Auto power-off choices supported by this headset's feature profile.
    /// </summary>
    public IReadOnlyList<string> DisplayedAutoPowerOffOptions =>
        Features.AutoPowerOffWhenRemoved ? AutoPowerOffOptions : TimedAutoPowerOffOptions;

    // =========================================================================
    // ERRORS
    // =========================================================================

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(PickerStatusText));
            }
        }
    }

    public bool HasError => _errorMessage is not null;

    /// <summary>
    /// The headphone list's status line: an error while one shows (the list has no warning
    /// bar), otherwise <see cref="StatusText"/>.
    /// </summary>
    public string PickerStatusText => _errorMessage ?? StatusText;

    // =========================================================================
    // METHODS
    // =========================================================================

    public void SelectNoiseMode(NoiseMode mode)
    {
        if (mode == _noiseMode)
        {
            return;
        }
        _noiseMode = mode;
        RaiseNoiseModeChanged();
        _ = SendNoiseControlAsync();
    }

    public void ApplyScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        _applying = true;
        try
        {
            _noiseMode = scene.Setting.Mode;
            if (scene.Setting.Mode == NoiseMode.Ambient)
            {
                AmbientLevel = scene.Setting.AmbientLevel;
            }
            FocusOnVoice = scene.Setting.FocusOnVoice;
        }
        finally
        {
            _applying = false;
        }
        RaiseNoiseModeChanged();
        OnPropertyChanged(nameof(AmbientLevelText));
        _ = SendNoiseControlAsync();
    }

    /// <summary>
    /// Asks the headset for fresh battery levels. Failures are logged, not shown.
    /// </summary>
    public async Task RefreshBatteryAsync()
    {
        if (!IsConnected)
        {
            return;
        }
        try
        {
            await _headset.RefreshBatteryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogMessages.BatteryRefreshFailed(_logger, ex, DeviceName);
        }
    }

    /// <summary>
    /// Settings changed outside this view model (the manager's Reconnect turns auto-connect back on).
    /// </summary>
    internal void RaiseAutoConnectChanged()
    {
        OnPropertyChanged(nameof(AutoConnect));
        OnPropertyChanged(nameof(IsReleased));
        OnPropertyChanged(nameof(IsAvailable));
    }

    internal void UpdateConnectionState(HeadsetConnectionState state)
    {
        // Windows connection and name changes arrive on the same event
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(IsWindowsConnected));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(ShowBattery));
        OnPropertyChanged(nameof(ShowReconnect));
        OnPropertyChanged(nameof(ShowConnect));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PickerStatusText));
        OnPropertyChanged(nameof(DisconnectedMessage));
        if (IsBluetoothConnecting && IsWindowsConnected)
        {
            _connectReachedWindows = true;
        }
        if (state == HeadsetConnectionState.Connected)
        {
            EndBluetoothConnect();
        }
        if (!SetProperty(ref _connectionState, state, nameof(ConnectionState)))
        {
            return;
        }
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanChangeConnectionMode));
        OnPropertyChanged(nameof(CanEditHeadsetSound));
        OnPropertyChanged(nameof(CanChangeSpatialEffect));
        OnPropertyChanged(nameof(CanPowerOffFromPopup));
        OnPropertyChanged(nameof(ShowPopupSoundSection));
        OnPropertyChanged(nameof(ShowPopupConnectionSeparator));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PickerStatusText));
        OnPropertyChanged(nameof(ShowStatus));
        OnPropertyChanged(nameof(ShowDisconnectedWarning));
        OnPropertyChanged(nameof(ShowReconnect));
        OnPropertyChanged(nameof(ShowPlayback));
        UpdateMissingEarbudPolling();
        if (state == HeadsetConnectionState.Connected)
        {
            ApplySnapshot(_headset.State);
        }
    }

    internal void RefreshScenes()
    {
        SceneItems = [.. _settings.Scenes.Select(scene => new SceneItemViewModel(scene))];
        UpdateActiveScene();
        OnPropertyChanged(nameof(SceneItems));
    }

    public void Dispose()
    {
        _lifetimeCancellation.Cancel();
        _headset.StateChanged -= OnHeadsetStateChanged;
        _noiseThrottler.Dispose();
        _errorTimer?.Dispose();
        _missingEarbudTimer?.Dispose();
        _connectTimer?.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private static string Percent(int? level) => level is null ? NoValue : $"{level}%";

    // The earbuds don't reliably announce an earbud coming back, so while one side is missing
    // the battery is re-read every few seconds until it reports again
    private void UpdateMissingEarbudPolling()
    {
        var oneMissing = IsConnected && Features.DualBattery && (_snapshot.Battery.Left is null ^ _snapshot.Battery.Right is null);
        if (!oneMissing)
        {
            _missingEarbudTimer?.Dispose();
            _missingEarbudTimer = null;
            return;
        }
        _missingEarbudTimer ??= _timeProvider.CreateTimer(_ => _ = RefreshBatteryAsync(), null, MissingEarbudPollInterval, MissingEarbudPollInterval);
    }

    private void OnHeadsetStateChanged(object? sender, HeadsetSnapshot snapshot) => _ui.Post(() => ApplySnapshot(snapshot));

    private void ApplySnapshot(HeadsetSnapshot snapshot)
    {
        var skipNoise = _noiseThrottler.HasPending || Volatile.Read(ref _noiseCommandsInFlight) > 0;
        var skipSpatial = _spatialCommandInFlight;

        // Taking an earbud out briefly reports neither side; keep the last levels until the
        // real ones follow, so the battery row doesn't blank out
        var battery = snapshot.Battery;
        if (Features.DualBattery && battery.Left is null && battery.Right is null && (_snapshot.Battery.Left is not null || _snapshot.Battery.Right is not null))
        {
            battery = battery with { Left = _snapshot.Battery.Left, Right = _snapshot.Battery.Right };
        }

        // The case only reports while an earbud sits in it; otherwise show the last level it
        // reported, even from an earlier run, like Sony's app does
        if (battery.Case is { } caseLevel)
        {
            _settings.SetLastCaseBattery(Id, caseLevel);
        }
        else if (Features.DualBattery)
        {
            battery = battery with { Case = _settings.GetLastCaseBattery(Id) };
        }
        snapshot = snapshot with { Battery = battery };
        _snapshot = snapshot;
        UpdateMissingEarbudPolling();
        _applying = true;
        try
        {
            if (!skipNoise)
            {
                _noiseMode = snapshot.NoiseControl.Mode;
                if (snapshot.NoiseControl.Mode == NoiseMode.Ambient && snapshot.NoiseControl.AmbientLevel > 0)
                {
                    AmbientLevel = snapshot.NoiseControl.AmbientLevel;
                }
                FocusOnVoice = snapshot.NoiseControl.FocusOnVoice;
            }

            SelectedEqualizerIndex = EqualizerPresets
                .Select((option, index) => (option, index))
                .FirstOrDefault(pair => pair.option.Value == snapshot.Equalizer.Preset, (null!, -1)).index;
            ClearBass = snapshot.Equalizer.ClearBass;
            Band1 = snapshot.Equalizer.Bands[0];
            Band2 = snapshot.Equalizer.Bands[1];
            Band3 = snapshot.Equalizer.Bands[2];
            Band4 = snapshot.Equalizer.Bands[3];
            Band5 = snapshot.Equalizer.Bands[4];
            DseeIndex =
                HasConnectionQualityControl &&
                !string.IsNullOrEmpty(snapshot.Codec) &&
                snapshot.Codec != "SBC" &&
                snapshot.Codec != "AAC"
                    ? 0
                    : snapshot.Dsee ? 1 : 0;
            ConnectionQualityIndex = HasConnectionQualityControl ? ConnectionModeIndexFor(snapshot) : -1;
            if (snapshot.VoiceGuidance is >= 0 and <= 1)
            {
                VoiceGuidance = snapshot.VoiceGuidance == 1;
            }
            if (!skipSpatial)
            {
                VptPresetIndex = snapshot.Vpt is >= 0 and <= 4 ? snapshot.Vpt : -1;
                SoundPositionIndex = Array.IndexOf(SoundPositionCodes, snapshot.SoundPosition);
            }
            SpeakToChat = snapshot.SpeakToChat;
            AdaptiveVolume = snapshot.AdaptiveVolume;
            AutoPowerOffIndex = snapshot.AutoPowerOff;
        }
        finally
        {
            _applying = false;
        }
        SetPlaybackDevices(snapshot.PlaybackDevices);

        RaiseNoiseModeChanged();
        OnPropertyChanged(nameof(AmbientLevelText));
        OnPropertyChanged(nameof(ShowDualBattery));
        OnPropertyChanged(nameof(ShowLeftBattery));
        OnPropertyChanged(nameof(ShowRightBattery));
        OnPropertyChanged(nameof(ShowSingleBattery));
        OnPropertyChanged(nameof(LeftBatteryText));
        OnPropertyChanged(nameof(RightBatteryText));
        OnPropertyChanged(nameof(CaseBatteryText));
        OnPropertyChanged(nameof(ShowCaseBattery));
        OnPropertyChanged(nameof(MainBatteryText));
        OnPropertyChanged(nameof(MainBatteryGlyph));
        OnPropertyChanged(nameof(Firmware));
        OnPropertyChanged(nameof(Codec));
        OnPropertyChanged(nameof(CanEditHeadsetSound));
        OnPropertyChanged(nameof(CanChangeSpatialEffect));
        OnPropertyChanged(nameof(CanPowerOffFromPopup));
        OnPropertyChanged(nameof(ShowPopupSoundSection));
        OnPropertyChanged(nameof(ShowPopupConnectionSeparator));
        OnPropertyChanged(nameof(AudioText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PickerStatusText));

        _lowBattery.Update(Id, DeviceName, snapshot.Battery);
    }

    /// <summary>
    /// Shows "Connecting…" until the control link opens, giving up with an error after
    /// <see cref="BluetoothConnectTimeout"/>.
    /// </summary>
    internal void StartBluetoothConnect()
    {
        IsBluetoothConnecting = true;
        _connectReachedWindows = false;
        _connectRetried = false;
        _connectTimer?.Dispose();
        _connectTimer = _timeProvider.CreateTimer(_ => _ui.Post(FailBluetoothConnect), null, BluetoothConnectTimeout, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// A headset still setting up can drop straight back off Windows. True, once per connect,
    /// when that just happened and the connect should be asked for again.
    /// </summary>
    internal bool TakeConnectRetry()
    {
        if (!IsBluetoothConnecting || !_connectReachedWindows || IsWindowsConnected || _connectRetried)
        {
            return false;
        }
        _connectRetried = true;
        _connectReachedWindows = false;
        return true;
    }

    /// <summary>
    /// Windows refused, or the connect never happened.
    /// </summary>
    internal void FailBluetoothConnect()
    {
        if (!IsBluetoothConnecting)
        {
            return;
        }
        EndBluetoothConnect();
        ShowError("Couldn't connect. Make sure your headphones are on and nearby.");
    }

    private void EndBluetoothConnect()
    {
        _connectTimer?.Dispose();
        _connectTimer = null;
        IsBluetoothConnecting = false;
    }

    private void SwitchPlayback(PlaybackDevice device)
    {
        if (device.Playing)
        {
            return;
        }

        // Show it straight away; a refusal (on a call, say) puts the headset's answer back
        SetPlaybackDevices([.. PlaybackDevices.Select(item => item with { Playing = item.Address == device.Address })]);
        _ = RunCommandAsync(
            () => _headset.SwitchPlaybackAsync(device.Address),
            "playback",
            "Couldn't switch the audio. The other device may be on a call.");
    }

    private void SetPlaybackDevices(IReadOnlyList<PlaybackDevice> devices)
    {
        if (devices.SequenceEqual(PlaybackDevices))
        {
            return;
        }
        PlaybackDevices = devices;
        OnPropertyChanged(nameof(PlaybackDevices));
        OnPropertyChanged(nameof(ShowPlayback));
    }

    private void RaiseNoiseModeChanged()
    {
        OnPropertyChanged(nameof(NoiseMode));
        OnPropertyChanged(nameof(IsNoiseOff));
        OnPropertyChanged(nameof(IsNoiseCancelling));
        OnPropertyChanged(nameof(IsAmbient));
        UpdateActiveScene();
    }

    private void UpdateActiveScene()
    {
        foreach (var item in SceneItems)
        {
            item.Update(_noiseMode, (int)_ambientLevel, _focusOnVoice);
        }
    }

    private Task SendNoiseControlAsync()
    {
        var setting = new NoiseControlSetting(_noiseMode, (int)_ambientLevel, _focusOnVoice);
        Interlocked.Increment(ref _noiseCommandsInFlight);
        return RunNoiseCommandAsync(setting);
    }

    // Headset echoes of earlier values are ignored while any noise command is in flight;
    // once the last one finishes, the controls take the headset's confirmed state.
    private async Task RunNoiseCommandAsync(NoiseControlSetting setting)
    {
        try
        {
            await RunCommandAsync(() => _headset.SetNoiseControlAsync(setting), "noise control").ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.Decrement(ref _noiseCommandsInFlight) == 0)
            {
                _ui.Post(() => ApplySnapshot(_headset.State));
            }
        }
    }

    private Task ApplyCustomEqualizerAsync()
    {
        if (HasConnectionQualityControl && !CanEditHeadsetSound)
        {
            return Task.CompletedTask;
        }

        var setting = new EqualizerSetting(
            EqualizerSetting.ManualPreset,
            (int)_clearBass,
            ImmutableArray.Create((int)_band1, (int)_band2, (int)_band3, (int)_band4, (int)_band5));
        return RunCommandAsync(() => _headset.SetEqualizerCustomAsync(setting), "custom equalizer");
    }

    // refusedMessage replaces the generic text when the headset answers "no" (a refused
    // multipoint switch comes back as an invalid-data error)
    private async Task RunCommandAsync(Func<Task> command, string setting, string? refusedMessage = null)
    {
        try
        {
            await command().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogMessages.CommandFailed(_logger, ex, setting, DeviceName);
            _ui.Post(() =>
            {
                ApplySnapshot(_headset.State);
                ShowError(refusedMessage is not null && ex.HResult == HeadsetErrorMessages.InvalidDataHResult
                    ? refusedMessage
                    : HeadsetErrorMessages.Describe(ex));
            });
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        _errorTimer?.Dispose();
        _errorTimer = _timeProvider.CreateTimer(_ => _ui.Post(() => ErrorMessage = null), null, ErrorDuration, Timeout.InfiniteTimeSpan);
    }
}
