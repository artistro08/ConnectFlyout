using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
/// One editable scene in the Scenes settings page, or the draft on the New scene page.
/// </summary>
public sealed class SceneEditorViewModel : ObservableObject
{
    private readonly Action? _changed;
    private string _name;
    private string _glyph;
    private int _modeIndex;
    private double _ambientLevel;
    private bool _focusOnVoice;
    private bool _showInFlyout;
    private bool _isActive;

    /// <param name="scene">The scene to start from.</param>
    /// <param name="changed">Called after every edit; the settings page saves on it. Null for a draft.</param>
    public SceneEditorViewModel(Scene scene, Action? changed = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        _changed = changed;
        Id = scene.Id ?? Scene.NewId();
        _name = scene.Name;
        _glyph = scene.Glyph;
        _modeIndex = (int)scene.Setting.Mode;
        _ambientLevel = Math.Clamp(scene.Setting.AmbientLevel, 1, 20);
        _focusOnVoice = scene.Setting.FocusOnVoice;
        _showInFlyout = scene.ShowInFlyout;
    }

    public static IReadOnlyList<string> ModeOptions { get; } = ["Off", "Noise canceling", "Ambient sound"];

    /// <summary>
    /// The scene's stable ID (see <see cref="Scene.Id"/>).
    /// </summary>
    public string Id { get; }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>
    /// A Segoe Fluent Icons glyph, picked from <see cref="SceneIcon.All"/>.
    /// </summary>
    public string Glyph
    {
        get => _glyph;
        set => Set(ref _glyph, value);
    }

    /// <summary>
    /// Index into <see cref="ModeOptions"/>, matching <see cref="NoiseMode"/>.
    /// </summary>
    public int ModeIndex
    {
        get => _modeIndex;
        set
        {
            if (Set(ref _modeIndex, value))
            {
                OnPropertyChanged(nameof(IsAmbient));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public bool IsAmbient => _modeIndex == (int)NoiseMode.Ambient;

    public double AmbientLevel
    {
        get => _ambientLevel;
        set
        {
            if (Set(ref _ambientLevel, Math.Clamp(Math.Round(value), 1, 20)))
            {
                OnPropertyChanged(nameof(AmbientLevelText));
                OnPropertyChanged(nameof(Summary));
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
        set
        {
            if (Set(ref _focusOnVoice, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    /// <summary>
    /// Whether the flyout shows this scene's button.
    /// </summary>
    public bool ShowInFlyout
    {
        get => _showInFlyout;
        set => Set(ref _showInFlyout, value);
    }

    /// <summary>
    /// Whether connected headphones are in this scene right now; the card's switch shows it.
    /// </summary>
    public bool IsActive => _isActive;

    internal void SetActive(bool value) => SetProperty(ref _isActive, value, nameof(IsActive));

    /// <summary>
    /// Re-announces <see cref="IsActive"/> so a switch that was flipped without effect snaps back.
    /// </summary>
    internal void NotifyActive() => OnPropertyChanged(nameof(IsActive));

    /// <summary>
    /// The card header's second line: the mode, plus the level and Focus on voice in Ambient sound.
    /// </summary>
    public string Summary => IsAmbient
        ? $"Ambient sound · Level {_ambientLevel:0}" + (_focusOnVoice ? " · Focus on voice" : "")
        : ModeOptions[Math.Clamp(_modeIndex, 0, 2)];

    public Scene ToScene()
    {
        var mode = (NoiseMode)Math.Clamp(_modeIndex, 0, 2);
        var level = mode == NoiseMode.Ambient ? (int)_ambientLevel : 0;
        var name = string.IsNullOrWhiteSpace(_name) ? "Scene" : _name.Trim();
        return new Scene(name, _glyph, new NoiseControlSetting(mode, level, _focusOnVoice), _showInFlyout, Id);
    }

    // SetProperty, then the save callback
    private bool Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name))
        {
            return false;
        }
        _changed?.Invoke();
        return true;
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
    private readonly TimeProvider _timeProvider;
    private readonly UiContext _ui = new();

    // Edits save this long after the last one, so a slider drag is one save, not one per step
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private bool _launchAtSignIn;
    private SceneEditorViewModel? _newScene;
    private ITimer? _saveTimer;

    public SettingsViewModel(
        FlyoutViewModel flyout,
        AppSettings settings,
        LogLevelSwitch logLevel,
        IStartupTaskService startup,
        Action<AppTheme> applyTheme,
        Action<bool> applyNativeDebugLogging,
        Action<string> openFolder,
        string logFolder,
        ILogger logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(flyout);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _flyout = flyout;
        _settings = settings;
        _logLevel = logLevel;
        _startup = startup;
        _applyTheme = applyTheme;
        _applyNativeDebugLogging = applyNativeDebugLogging;
        _openFolder = openFolder;
        _logger = logger;
        _timeProvider = timeProvider;
        LogFolder = logFolder;

        Scenes = [.. settings.Scenes.Select(scene => new SceneEditorViewModel(scene, ScheduleSave))];
        ResetScenesCommand = new RelayCommand(ResetScenes);
        OpenLogFolderCommand = new RelayCommand(() => _openFolder(LogFolder));

        // Each headset's state lights or dims the scene switches
        foreach (var headset in Headsets)
        {
            headset.PropertyChanged += OnHeadsetChanged;
        }
        RefreshActiveScenes();
        _flyout.Headsets.CollectionChanged += (_, e) =>
        {
            foreach (var headset in e.OldItems?.OfType<HeadsetViewModel>() ?? [])
            {
                headset.PropertyChanged -= OnHeadsetChanged;
            }
            foreach (var headset in e.NewItems?.OfType<HeadsetViewModel>() ?? [])
            {
                headset.PropertyChanged += OnHeadsetChanged;
            }
            RefreshActiveScenes();
            OnPropertyChanged(nameof(NoHeadset));
        };
    }

    /// <summary>
    /// A scene was added, deleted or reset, so the registered shortcuts need another look.
    /// </summary>
    public event EventHandler? ScenesChanged;

    // =========================================================================
    // HEADSETS
    // =========================================================================

    public ObservableCollection<HeadsetViewModel> Headsets => _flyout.Headsets;

    /// <summary>
    /// No paired Sony headphones yet; the Devices page says where to pair them.
    /// </summary>
    public bool NoHeadset => Headsets.Count == 0;

    /// <summary>
    /// Saves the headsets' current order (the Reorder headphones page dragged them).
    /// </summary>
    public void SaveHeadsetOrder() => _settings.HeadsetOrder = [.. Headsets.Select(headset => headset.Id)];

    // =========================================================================
    // SCENES
    // =========================================================================

    /// <summary>
    /// The saved scenes. Each one saves itself after every edit.
    /// </summary>
    public ObservableCollection<SceneEditorViewModel> Scenes { get; }

    public IRelayCommand ResetScenesCommand { get; }

    /// <summary>
    /// The draft on the New scene page; null between visits. Nothing is saved until
    /// <see cref="CommitNewScene"/>.
    /// </summary>
    public SceneEditorViewModel? NewScene
    {
        get => _newScene;
        private set => SetProperty(ref _newScene, value);
    }

    /// <summary>
    /// Starts a draft for the New scene page: Noise canceling, the first icon, shown in the flyout.
    /// </summary>
    public void BeginNewScene() =>
        NewScene = new SceneEditorViewModel(new Scene("", SceneIcon.All[0].Glyph, new NoiseControlSetting(NoiseMode.NoiseCancelling, 10, false)));

    /// <summary>
    /// Adds the draft to the scenes and saves. Does nothing without a draft.
    /// </summary>
    public void CommitNewScene()
    {
        if (NewScene is null)
        {
            return;
        }
        Scenes.Add(new SceneEditorViewModel(NewScene.ToScene(), ScheduleSave));
        NewScene = null;
        SaveScenes();
        ScenesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelNewScene() => NewScene = null;

    /// <summary>
    /// Applies the scene to every connected pair of headphones, as its flyout button would.
    /// </summary>
    public void ActivateScene(SceneEditorViewModel scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var applied = scene.ToScene();
        foreach (var headset in ConnectedHeadsets)
        {
            headset.ApplySceneCommand.Execute(applied);
        }
    }

    private IEnumerable<HeadsetViewModel> ConnectedHeadsets => Headsets.Where(headset => headset.IsConnected);

    /// <summary>
    /// The card's switch: on applies the scene to the connected headphones, off turns their
    /// noise control off. A switch that already matches does nothing, which is how its own
    /// binding updates arrive. With nothing connected the switch snaps back.
    /// </summary>
    public void ToggleScene(SceneEditorViewModel scene, bool on)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (on == scene.IsActive)
        {
            return;
        }
        if (on)
        {
            ActivateScene(scene);
        }
        else
        {
            foreach (var headset in ConnectedHeadsets)
            {
                headset.SetNoiseModeCommand.Execute(nameof(NoiseMode.Off));
            }
        }
        RefreshActiveScenes();
        scene.NotifyActive();
    }

    // Only the parts of a headset's state a scene is judged by, not the dozens of others a snapshot raises
    private void OnHeadsetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HeadsetViewModel.NoiseMode) or nameof(HeadsetViewModel.AmbientLevel)
            or nameof(HeadsetViewModel.FocusOnVoice) or nameof(HeadsetViewModel.IsConnected))
        {
            RefreshActiveScenes();
        }
    }

    private void RefreshActiveScenes()
    {
        foreach (var scene in Scenes)
        {
            var applied = scene.ToScene();
            scene.SetActive(Headsets.Any(headset => headset.IsConnected && headset.MatchesScene(applied)));
        }
    }

    public void DeleteScene(SceneEditorViewModel scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (Scenes.Remove(scene))
        {
            _settings.SetSceneShortcut(scene.Id, null);
            SaveScenes();
            ScenesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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

    public bool HideTray
    {
        get => _settings.HideTray;
        set
        {
            if (value == _settings.HideTray)
            {
                return;
            }
            _settings.HideTray = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanHideTrayWhileDisconnected));
        }
    }

    /// <summary>
    /// "Until the headphones connect" only matters while the icon isn't hidden for good.
    /// </summary>
    public bool CanHideTrayWhileDisconnected => !HideTray;

    public bool HideTrayWhileDisconnected
    {
        get => _settings.HideTrayWhileDisconnected;
        set
        {
            if (value == _settings.HideTrayWhileDisconnected)
            {
                return;
            }
            _settings.HideTrayWhileDisconnected = value;
            OnPropertyChanged();
        }
    }

    // Hidden Tray Launch: the dropdown's choices, in HiddenTrayLaunch order
    public static IReadOnlyList<string> HiddenTrayLaunchOptions { get; } = ["Open Settings", "Open the flyout", "Do nothing"];

    // Hidden Tray Launch: the dropdown's selection
    public int HiddenTrayLaunchIndex
    {
        get => (int)_settings.HiddenTrayLaunch;
        set
        {
            if (!Enum.IsDefined((HiddenTrayLaunch)value) || value == (int)_settings.HiddenTrayLaunch)
            {
                return;
            }
            _settings.HiddenTrayLaunch = (HiddenTrayLaunch)value;
            OnPropertyChanged();
        }
    }

    // Hide Flyout Controls: each switch saves, then tells every headset page which sections to redraw
    public bool HideAmbientControls
    {
        get => _settings.HideAmbientControls;
        set
        {
            if (value == _settings.HideAmbientControls)
            {
                return;
            }
            _settings.HideAmbientControls = value;
            OnPropertyChanged();
            RefreshFlyoutSections(nameof(HeadsetViewModel.ShowAmbientControls));
        }
    }

    public bool HideScenes
    {
        get => _settings.HideScenes;
        set
        {
            if (value == _settings.HideScenes)
            {
                return;
            }
            _settings.HideScenes = value;
            OnPropertyChanged();
            RefreshFlyoutSections(nameof(HeadsetViewModel.ShowScenes));
        }
    }

    public bool HidePlayback
    {
        get => _settings.HidePlayback;
        set
        {
            if (value == _settings.HidePlayback)
            {
                return;
            }
            _settings.HidePlayback = value;
            OnPropertyChanged();
            RefreshFlyoutSections(nameof(HeadsetViewModel.ShowPlayback));
        }
    }

    public bool HideSoundControls
    {
        get => _settings.HideSoundControls;
        set
        {
            if (value == _settings.HideSoundControls)
            {
                return;
            }
            _settings.HideSoundControls = value;
            OnPropertyChanged();
            RefreshFlyoutSections(
                nameof(HeadsetViewModel.ShowPopupSoundSection),
                nameof(HeadsetViewModel.ShowPopupConnectionSeparator),
                nameof(HeadsetViewModel.ShowPopupSoundSeparator));
        }
    }

    private void RefreshFlyoutSections(params string[] sections)
    {
        foreach (var headset in Headsets)
        {
            headset.RefreshFlyoutSections(sections);
        }
    }
    // /Hide Flyout Controls

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

    /// <summary>
    /// Saves the scenes as they are, in their current order, and refreshes the flyout's buttons.
    /// Each edit calls this on its own; the Reorder scenes page calls it after a drag.
    /// </summary>
    public void SaveScenes()
    {
        _settings.Scenes = [.. Scenes.Select(scene => scene.ToScene())];
        foreach (var headset in Headsets)
        {
            headset.RefreshScenes();
        }
        RefreshActiveScenes();
    }

    // The defaults keep their IDs, so their shortcuts survive a reset; the other scenes' shortcuts go with them
    private void ResetScenes()
    {
        foreach (var scene in Scenes.Where(scene => Scene.Defaults.All(standard => standard.Id != scene.Id)))
        {
            _settings.SetSceneShortcut(scene.Id, null);
        }
        Scenes.Clear();
        foreach (var scene in Scene.Defaults)
        {
            Scenes.Add(new SceneEditorViewModel(scene, ScheduleSave));
        }
        SaveScenes();
        ScenesChanged?.Invoke(this, EventArgs.Empty);
    }

    // An edit's save, a moment after the last edit
    private void ScheduleSave()
    {
        _saveTimer ??= _timeProvider.CreateTimer(_ => _ui.Post(SaveScenes), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }
}
