using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ConnectFlyout.Presentation.Common;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Scenes;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.ViewModels;

/// <summary>
/// One editable scene in the Noise &amp; scenes settings page.
/// </summary>
public sealed class SceneEditorViewModel : ObservableObject
{
    private string _name;
    private int _modeIndex;
    private double _ambientLevel;
    private bool _focusOnVoice;

    public SceneEditorViewModel(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Glyph = scene.Glyph;
        _name = scene.Name;
        _modeIndex = (int)scene.Setting.Mode;
        _ambientLevel = Math.Clamp(scene.Setting.AmbientLevel, 1, 20);
        _focusOnVoice = scene.Setting.FocusOnVoice;
    }

    public static IReadOnlyList<string> ModeOptions { get; } = ["Off", "Noise cancelling", "Ambient sound"];

    public string Glyph { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Index into <see cref="ModeOptions"/>, matching <see cref="NoiseMode"/>.
    /// </summary>
    public int ModeIndex
    {
        get => _modeIndex;
        set
        {
            if (SetProperty(ref _modeIndex, value))
            {
                OnPropertyChanged(nameof(IsAmbient));
            }
        }
    }

    public bool IsAmbient => _modeIndex == (int)NoiseMode.Ambient;

    public double AmbientLevel
    {
        get => _ambientLevel;
        set
        {
            if (SetProperty(ref _ambientLevel, Math.Clamp(Math.Round(value), 1, 20)))
            {
                OnPropertyChanged(nameof(AmbientLevelText));
            }
        }
    }

    /// <summary>
    /// The level as shown beside the slider, like the flyout's.
    /// </summary>
    public string AmbientLevelText => $"{_ambientLevel:0}";

    public bool FocusOnVoice
    {
        get => _focusOnVoice;
        set => SetProperty(ref _focusOnVoice, value);
    }

    public Scene ToScene()
    {
        var mode = (NoiseMode)Math.Clamp(_modeIndex, 0, 2);
        var level = mode == NoiseMode.Ambient ? (int)_ambientLevel : 0;
        var name = string.IsNullOrWhiteSpace(_name) ? "Scene" : _name.Trim();
        return new Scene(name, Glyph, new NoiseControlSetting(mode, level, _focusOnVoice));
    }
}

/// <summary>
/// The settings window.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly FlyoutViewModel _flyout;
    private readonly AppSettings _settings;
    private readonly LogLevelSwitch _logLevel;
    private readonly IStartupTaskService _startup;
    private readonly Action<AppTheme> _applyTheme;
    private readonly Action<bool> _applyNativeDebugLogging;
    private readonly Action<string> _openFolder;
    private readonly ILogger _logger;
    private readonly UiContext _ui = new();

    private int _selectedHeadsetIndex;
    private bool _launchAtSignIn;

    public SettingsViewModel(
        FlyoutViewModel flyout,
        AppSettings settings,
        LogLevelSwitch logLevel,
        IStartupTaskService startup,
        Action<AppTheme> applyTheme,
        Action<bool> applyNativeDebugLogging,
        Action<string> openFolder,
        string logFolder,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(flyout);

        _flyout = flyout;
        _settings = settings;
        _logLevel = logLevel;
        _startup = startup;
        _applyTheme = applyTheme;
        _applyNativeDebugLogging = applyNativeDebugLogging;
        _openFolder = openFolder;
        _logger = logger;
        LogFolder = logFolder;

        Scenes = [.. settings.Scenes.Select(scene => new SceneEditorViewModel(scene))];
        SaveScenesCommand = new RelayCommand(SaveScenes);
        ResetScenesCommand = new RelayCommand(ResetScenes);
        OpenLogFolderCommand = new RelayCommand(() => _openFolder(LogFolder));

        // Deferred so a ComboBox bound to Headsets gets the change before SelectedHeadsetIndex moves.
        _flyout.Headsets.CollectionChanged += (_, _) => _ui.Defer(OnHeadsetsChanged);
        _selectedHeadsetIndex = Headsets.Count > 0 ? 0 : -1;
    }

    // =========================================================================
    // HEADSETS
    // =========================================================================

    public ObservableCollection<HeadsetViewModel> Headsets => _flyout.Headsets;

    public int SelectedHeadsetIndex
    {
        get => _selectedHeadsetIndex;
        set
        {
            if (SetProperty(ref _selectedHeadsetIndex, value))
            {
                OnPropertyChanged(nameof(SelectedHeadset));
                OnPropertyChanged(nameof(HasHeadset));
                OnPropertyChanged(nameof(NoHeadset));
                OnPropertyChanged(nameof(NoSystemOptions));
            }
        }
    }

    public HeadsetViewModel? SelectedHeadset =>
        _selectedHeadsetIndex >= 0 && _selectedHeadsetIndex < Headsets.Count ? Headsets[_selectedHeadsetIndex] : null;

    public bool HasHeadset => SelectedHeadset is not null;

    public bool NoHeadset => SelectedHeadset is null;

    /// <summary>
    /// The selected headset has nothing for the System page.
    /// </summary>
    public bool NoSystemOptions =>
        SelectedHeadset is { } headset &&
        !headset.Features.AutoPowerOff &&
        !headset.Features.AdaptiveVolume &&
        !headset.HasVoiceGuidanceControl &&
        !headset.HasConnectionQualityControl;

    /// <summary>
    /// Selects the headset the flyout is showing, so settings open on the same headphones.
    /// Called when the settings window opens; after that the choice stays with the window.
    /// </summary>
    public void SelectCurrentHeadset()
    {
        var index = _flyout.CurrentHeadset is { } current ? Headsets.IndexOf(current) : -1;
        if (index < 0)
        {
            return;
        }
        SelectedHeadsetIndex = index;
    }

    // =========================================================================
    // SCENES
    // =========================================================================

    public ObservableCollection<SceneEditorViewModel> Scenes { get; }

    public IRelayCommand SaveScenesCommand { get; }

    public IRelayCommand ResetScenesCommand { get; }

    // =========================================================================
    // APP
    // =========================================================================

    public bool LaunchAtSignIn
    {
        get => _launchAtSignIn;
        set
        {
            if (SetProperty(ref _launchAtSignIn, value))
            {
                _ = ApplyLaunchAtSignInAsync(value);
            }
        }
    }

    public bool LowBatteryNotifications
    {
        get => _settings.LowBatteryNotifications;
        set
        {
            if (value == _settings.LowBatteryNotifications)
            {
                return;
            }
            _settings.LowBatteryNotifications = value;
            OnPropertyChanged();
        }
    }

    public static IReadOnlyList<string> ThemeOptions { get; } = ["Use system setting", "Light", "Dark"];

    public int ThemeIndex
    {
        get => (int)_settings.Theme;
        set
        {
            if (value < 0 || value == (int)_settings.Theme)
            {
                return;
            }
            _settings.Theme = (AppTheme)value;
            _applyTheme(_settings.Theme);
            OnPropertyChanged();
        }
    }

    public bool DebugLogging
    {
        get => _settings.DebugLogging;
        set
        {
            if (value == _settings.DebugLogging)
            {
                return;
            }
            _settings.DebugLogging = value;
            ApplyLogLevel();
            OnPropertyChanged();
        }
    }

    public string LogFolder { get; }

    public IRelayCommand OpenLogFolderCommand { get; }

    /// <summary>
    /// Reads the startup task state. Called when the window opens.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            _launchAtSignIn = await _startup.IsEnabledAsync().ConfigureAwait(true);
            OnPropertyChanged(nameof(LaunchAtSignIn));
        }
        catch (Exception ex)
        {
            LogMessages.StartupTaskReadFailed(_logger, ex);
        }
    }

    /// <summary>
    /// Applies the saved Debug logging choice to both loggers. Called at startup.
    /// </summary>
    public void ApplyLogLevel()
    {
        _logLevel.MinimumLevel = _settings.DebugLogging ? LogLevel.Debug : LogLevel.Information;
        _applyNativeDebugLogging(_settings.DebugLogging);
    }

    private async Task ApplyLaunchAtSignInAsync(bool enabled)
    {
        try
        {
            var actual = await _startup.SetEnabledAsync(enabled).ConfigureAwait(true);
            if (actual != _launchAtSignIn)
            {
                _launchAtSignIn = actual;
                OnPropertyChanged(nameof(LaunchAtSignIn));
            }
        }
        catch (Exception ex)
        {
            LogMessages.StartupTaskChangeFailed(_logger, ex);
        }
    }

    private void SaveScenes()
    {
        _settings.Scenes = [.. Scenes.Select(scene => scene.ToScene())];
        foreach (var headset in Headsets)
        {
            headset.RefreshScenes();
        }
    }

    private void ResetScenes()
    {
        Scenes.Clear();
        foreach (var scene in Scene.Defaults)
        {
            Scenes.Add(new SceneEditorViewModel(scene));
        }
        SaveScenes();
    }

    private void OnHeadsetsChanged()
    {
        if (_selectedHeadsetIndex >= Headsets.Count || (_selectedHeadsetIndex < 0 && Headsets.Count > 0))
        {
            SelectedHeadsetIndex = Headsets.Count > 0 ? 0 : -1;
            return;
        }
        OnPropertyChanged(nameof(SelectedHeadset));
        OnPropertyChanged(nameof(HasHeadset));
        OnPropertyChanged(nameof(NoHeadset));
        OnPropertyChanged(nameof(NoSystemOptions));
    }
}
