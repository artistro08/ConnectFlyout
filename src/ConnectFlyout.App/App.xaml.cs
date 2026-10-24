using System.Diagnostics;
using System.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using ConnectFlyout.App.Interop;
using ConnectFlyout.App.Views.Settings;
using ConnectFlyout.Core;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Navigation;
using ConnectFlyout.Presentation.Notifications;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.Shortcuts;
using ConnectFlyout.Presentation.ViewModels;
using Windows.Win32;

namespace ConnectFlyout.App;

/// <summary>
/// Tray app composition root. No window opens at launch; the tray icon opens the flyout.
/// </summary>
public partial class App : Application, IDisposable
{
    private ILoggerFactory? _loggerFactory;
    private ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    private AppSettings? _settings;
    private AppNotificationService? _notifications;
    private HeadsetManager? _manager;
    private FlyoutViewModel? _flyout;
    private readonly WindowsBluetoothAudio _bluetoothAudio = new();
    private SettingsViewModel? _settingsViewModel;
    private FlyoutWindow? _flyoutWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;
    private TrayMenuWindow? _trayMenu;
    private TrayTooltipWindow? _trayTooltip;
    private GlobalShortcuts? _shortcuts;
    private EventWaitHandle? _quitRequest;
    private RegisteredWaitHandle? _quitWait;
    private RegisteredWaitHandle? _showWait;
    private bool _quitting;
    private bool _openingWhileHidden;

    private static readonly TimeSpan EnumerationTimeout = TimeSpan.FromSeconds(5);

    // Memory Trim: soon after startup, and only after a long idle otherwise, since a trim pages the
    // whole process out and the next open pays to bring it back
    private static readonly TimeSpan StartupTrimDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleTrimDelay = TimeSpan.FromMinutes(10);
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _trimTimer;

    public App()
    {
        InitializeComponent();

        // Closing the settings window must not end a tray app.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => AppLog.Unhandled(_logger, e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppIdentity.ClaimClassicAppId();
        AppIdentity.MigrateFromSonyControl();

        // Logging
        var logFolder = Path.Combine(AppIdentity.DataFolder, "Logs");
        var levelSwitch = new LogLevelSwitch();
        _loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new RollingFileLoggerProvider(logFolder, levelSwitch, TimeProvider.System)));
        _logger = _loggerFactory.CreateLogger("ConnectFlyout");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Unhandled(_logger, e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.UnobservedTask(_logger, e.Exception);
            e.SetObserved();
        };

        var nativeLogger = _loggerFactory.CreateLogger("ConnectFlyout.Native");
        HeadsetClient.SetLogHandler((level, message) =>
        {
            var logLevel = ToLogLevel(level);
            AppLog.Native(nativeLogger, logLevel, message);
        });

        // Services
        var time = TimeProvider.System;
        // MSIX keeps settings in its package store; the classic install uses a file
        ISettingsStore settingsStore = AppIdentity.IsPackaged
            ? new LocalSettingsStore()
            : new JsonFileSettingsStore(Path.Combine(AppIdentity.DataFolder, "settings.json"));
        _settings = new AppSettings(settingsStore);
        _notifications = new AppNotificationService(_loggerFactory.CreateLogger<AppNotificationService>());
        _notifications.Register();
        var lowBattery = new LowBatteryMonitor(_settings, _notifications);

        _manager = new HeadsetManager(
            new BluetoothDeviceWatcher(),
            name => new WinRtHeadset(name),
            _settings,
            time,
            _loggerFactory.CreateLogger<HeadsetManager>())
        {
            WindowsConnectSettle = TimeSpan.FromSeconds(1.5),
        };

        // View Models
        var headsetLogger = _loggerFactory.CreateLogger<HeadsetViewModel>();
        _flyout = new FlyoutViewModel(
            _manager,
            new FlyoutNavigator(_settings),
            managed => new HeadsetViewModel(managed, _settings, lowBattery, time, headsetLogger),
            _bluetoothAudio,
            time);
        _flyout.SettingsRequested += (_, _) => ShowSettings();
        _flyout.QuitRequested += (_, _) => Quit();

        // Startup Entry: the classic install's sign-in entry from before --startup gets it now, so
        // the next sign-in doesn't count as being opened by hand
        IStartupTaskService startupService = AppIdentity.IsPackaged ? new StartupTaskService() : new RegistryStartupService(Environment.ProcessPath!);
        (startupService as RegistryStartupService)?.UpgradeEntry();

        _settingsViewModel = new SettingsViewModel(
            _flyout,
            _settings,
            levelSwitch,
            startupService,
            ApplyTheme,
            HeadsetClient.SetDebugLogging,
            OpenFolder,
            logFolder,
            _loggerFactory.CreateLogger<SettingsViewModel>());
        _settingsViewModel.ApplyLogLevel();

        // Tray Icon and Flyout
        _flyoutWindow = new FlyoutWindow(_flyout);
        _flyoutWindow.CloseRequested += (_, _) => _flyoutWindow.HideFlyout();

        _trayIcon = new TrayIcon("ConnectFlyout");
        _trayIcon.Invoked += (_, _) => _flyoutWindow.Toggle(_trayIcon.GetIconRect());
        _trayIcon.ContextMenuRequested += (_, point) => ShowTrayMenu(point.X, point.Y);

        // Memory Trim: once the flyout has been hidden a while, hand what it used back to Windows
        _flyoutWindow.AppWindow.Changed += (sender, e) =>
        {
            if (e.DidVisibilityChange && !sender.IsVisible)
            {
                ScheduleMemoryTrim(IdleTrimDelay);
            }
        };

        // Notification Clicks open the flyout on the headset the notification was about
        _notifications.HeadsetClicked += (_, headsetId) => _flyoutWindow.DispatcherQueue.TryEnqueue(() =>
        {
            _flyout.ShowHeadset(headsetId);
            _flyoutWindow.Open(_trayIcon.GetIconRect());
        });

        // Hover Tooltip: connected headsets and their batteries; not over the open flyout
        _trayTooltip = new TrayTooltipWindow(_flyout);
        _trayIcon.PopupOpening += (_, _) => ShowTrayTooltip();
        _trayIcon.PopupClosing += (_, _) => _trayTooltip.Hide();
        _trayIcon.Invoked += (_, _) => _trayTooltip.Hide();
        _trayIcon.ContextMenuRequested += (_, _) => _trayTooltip.Hide();

        // Tray Visibility: "Hide the tray icon" hides it for good; "until my headphones connect"
        // shows it only while Windows has headphones connected
        _manager.ConnectionStateChanged += (_, _) => _flyoutWindow.DispatcherQueue.TryEnqueue(UpdateTrayVisibility);
        _manager.HeadsetRemoved += (_, _) => _flyoutWindow.DispatcherQueue.TryEnqueue(UpdateTrayVisibility);
        _settingsViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.HideTray) or nameof(SettingsViewModel.HideTrayWhileDisconnected))
            {
                UpdateTrayVisibility();
            }
        };
        UpdateTrayVisibility();

        // Global Shortcuts: registered on the tray icon's window; each switches the shown headphones' noise mode
        _shortcuts = new GlobalShortcuts(_loggerFactory.CreateLogger<GlobalShortcuts>());
        _trayIcon.HotkeyPressed += (_, id) => _shortcuts.OnHotkey(id);
        _shortcuts.Pressed += (_, action) => OnShortcutPressed(action);
        _shortcuts.Attach(_trayIcon.Handle, _settings);

        ApplyTheme(_settings.Theme);
        _manager.Start();
        WatchForQuitRequests();
        WatchForShowRequests();
        AppLog.Started(_logger, AppIdentity.Version);

        // Startup itself leaves a lot behind that the idle tray app never needs
        ScheduleMemoryTrim(StartupTrimDelay);
    }

    private static LogLevel ToLogLevel(NativeLogLevel level) => level switch
    {
        NativeLogLevel.Debug => LogLevel.Debug,
        NativeLogLevel.Warning => LogLevel.Warning,
        NativeLogLevel.Error => LogLevel.Error,
        _ => LogLevel.Information,
    };

    // ConnectFlyout.exe --quit signals this event (see Program); quit like the tray menu does
    private void WatchForQuitRequests()
    {
        _quitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, Program.QuitEventName);
        _quitWait = WatchEvent(_quitRequest, Quit, once: true);
    }

    // Opening ConnectFlyout by hand signals this event, this copy included (see Program)
    private void WatchForShowRequests()
    {
        if (Program.ShowRequest is not null)
        {
            _showWait = WatchEvent(Program.ShowRequest, () => _ = OpenWhileTrayHiddenAsync(), once: false);
        }
    }

    // Runs an action on the UI thread each time a named event is signaled
    private static RegisteredWaitHandle WatchEvent(WaitHandle handle, Action action, bool once)
    {
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        return ThreadPool.RegisterWaitForSingleObject(
            handle,
            (_, _) => dispatcher.TryEnqueue(() => action()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: once);
    }

    /// <summary>
    /// Does what "When I open ConnectFlyout while the icon is hidden" says, if the icon is hidden.
    /// </summary>
    /// <remarks>
    /// Waits for Windows to list the paired headphones first (up to a few seconds), so connected
    /// headphones that are about to bring the icon back don't count as hidden.
    /// </remarks>
    private async Task OpenWhileTrayHiddenAsync()
    {
        // One at a time: a second launch during startup must not open things twice
        if (_quitting || _openingWhileHidden || _settings is null || _manager is null || _flyoutWindow is null)
        {
            return;
        }
        _openingWhileHidden = true;

        try
        {
            // Only "until my headphones connect" depends on what's connected
            if (_settings.HideTrayWhileDisconnected && !_settings.HideTray)
            {
                try
                {
                    await _manager.Enumerated.WaitAsync(EnumerationTimeout);
                }
                catch (TimeoutException)
                {
                    // Go on with the headphones known so far
                }
            }

            if (_quitting || _settings.ShowsTrayIcon(AnyHeadphonesConnected()))
            {
                return;
            }

            var action = _settings.HiddenTrayLaunch;

            // Never Locked Out: "Do nothing" with the icon hidden (for good, or with no headphones
            // to bring it back) and no working Open the flyout shortcut would leave no way back in
            if (action == HiddenTrayLaunch.Nothing && _shortcuts?.IsRegistered(ShortcutAction.OpenFlyout) != true)
            {
                action = HiddenTrayLaunch.OpenSettings;
            }

            switch (action)
            {
                case HiddenTrayLaunch.OpenSettings:
                    ShowSettings();
                    break;

                // No icon to sit by, so on the screen the mouse is on
                case HiddenTrayLaunch.OpenFlyout:
                    _flyoutWindow.Open(null);
                    break;
            }
        }
        finally
        {
            _openingWhileHidden = false;
        }
    }

    // Headphones Connected: any paired headphones Windows has connected right now
    private bool AnyHeadphonesConnected() => _manager?.Headsets.Any(headset => headset.IsWindowsConnected) ?? false;

    // Tray Visibility: shows or hides the icon to match the settings and connected headphones
    private void UpdateTrayVisibility()
    {
        // A change queued just before quitting mustn't bring the icon back
        if (_quitting || _trayIcon is null || _settings is null)
        {
            return;
        }

        if (_settings.ShowsTrayIcon(AnyHeadphonesConnected()))
        {
            _trayIcon.Show();
        }
        // Only as the icon goes: a flyout opened by shortcut while it's hidden stays open
        else if (_trayIcon.Hide())
        {
            _flyoutWindow?.HideFlyout();
            _trayTooltip?.Hide();
            _trayMenu?.HideMenu();
        }

        // Settings' Quit stands in for the tray menu's while the icon is gone
        _settingsWindow?.ShowQuit(!_trayIcon.IsVisible);
    }

    // The headphones the flyout shows, or else the first connected ones
    private void OnShortcutPressed(ShortcutAction action)
    {
        var name = action.ToString();

        // Open The Flyout: by the tray icon, or with Windhawk's tray on every taskbar, on the screen the mouse is on
        if (action == ShortcutAction.OpenFlyout)
        {
            AppLog.ShortcutPressed(_logger, name, true);
            if (_flyoutWindow is not null && _trayIcon is not null)
            {
                _flyoutWindow.Toggle(Windhawk.HasTrayOnEveryTaskbar() ? null : _trayIcon.GetIconRect());
            }
            return;
        }

        var connected = _flyout?.ConnectedHeadsets ?? [];
        var headset = _flyout?.CurrentHeadset is { IsConnected: true } current
            ? current
            : connected.Count > 0 ? connected[0] : null;
        AppLog.ShortcutPressed(_logger, name, headset is not null);
        if (headset is null)
        {
            return;
        }

        // Already On: the headphones stay silent for a repeat of the current mode, so the PC plays a
        // sound. Judged by the mode they last reported, so a second press of a new mode while the
        // first is still on its way doesn't also get the sound.
        var mode = ShortcutActions.NoiseModeFor(action);
        if (headset.ConfirmedNoiseMode == mode)
        {
            _ = PlayModeAlreadyOnAsync(headset.Id);
        }
        headset.SelectNoiseMode(mode, resend: true);
    }

    private static readonly string ModeAlreadyOnSound = Path.Combine(AppContext.BaseDirectory, "Assets", "ModeAlreadyOn.wav");

    // Plays straight to the headphones' stereo output, whatever Windows' default device is
    private async Task PlayModeAlreadyOnAsync(string bluetoothAddress)
    {
        try
        {
            await _bluetoothAudio.PlaySoundAsync(bluetoothAddress, ModeAlreadyOnSound);
        }
        catch (Exception ex)
        {
            AppLog.SoundFailed(_logger, ex);
        }
    }

    // The right-click menu's window is made on first use, since most sessions never open it
    private void ShowTrayMenu(int pointX, int pointY)
    {
        _flyoutWindow?.HideFlyout();
        if (_flyout is null)
        {
            return;
        }

        if (_trayMenu is null)
        {
            _trayMenu = new TrayMenuWindow();
            _trayMenu.ApplyTheme(_settings?.Theme ?? AppTheme.System);
            _trayMenu.SettingsRequested += (_, _) => ShowSettings();
            _trayMenu.QuitRequested += (_, _) => Quit();
        }

        var (x, y, edge, _, _) = ScreenGeometry.GetMenuAnchor(pointX, pointY);
        _trayMenu.ShowAt(x, y, edge, _flyout.Headsets);
    }

    // Waits so a quick reopen doesn't pay for it, and so closed windows finish tearing down. A new
    // schedule replaces a pending one.
    private void ScheduleMemoryTrim(TimeSpan delay)
    {
        if (_trimTimer is null)
        {
            _trimTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _trimTimer.IsRepeating = false;
            _trimTimer.Tick += (_, _) => TrimMemory();
        }
        _trimTimer.Stop();
        _trimTimer.Interval = delay;
        _trimTimer.Start();
    }

    // Only while nothing is on screen: a full compacting collection, then pages nothing is
    // using leave the working set (Windows pages them back in if they're needed again)
    private void TrimMemory()
    {
        if (_flyoutWindow?.AppWindow.IsVisible == true || _settingsWindow is not null)
        {
            return;
        }

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        // Finalizers release the WinRT objects behind closed windows; collect what they let go
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _ = PInvoke.SetProcessWorkingSetSize(PInvoke.GetCurrentProcess(), nuint.MaxValue, nuint.MaxValue);
    }

    private void ShowTrayTooltip()
    {
        if (_trayIcon?.GetIconRect() is not { } icon || _trayTooltip is null || _flyoutWindow?.AppWindow.IsVisible == true)
        {
            return;
        }

        // Taskbar side and work area of the icon's monitor; the tooltip itself follows the mouse
        var (_, _, edge, workArea, scale) = ScreenGeometry.GetMenuAnchor((icon.Left + icon.Right) / 2, (icon.Top + icon.Bottom) / 2);
        _trayTooltip.Open(edge, workArea, scale);
    }

    private void ShowSettings()
    {
        _flyoutWindow?.HideFlyout();
        if (_settingsViewModel is null || _settings is null || _shortcuts is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsViewModel, new ShortcutsContext(_shortcuts, _settings));
            _settingsWindow.ApplyTheme(_settings.Theme);
            _settingsWindow.QuitRequested += (_, _) => Quit();
            _settingsWindow.ShowQuit(_trayIcon?.IsVisible != true);
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;

                // Quitting from Settings closes it too; no trim on the way out
                if (!_quitting)
                {
                    ScheduleMemoryTrim(IdleTrimDelay);
                }
            };

            // Open on the flyout's headphones; once open, the window keeps its own choice
            _settingsViewModel.SelectCurrentHeadset();
        }
        _settingsWindow.Activate();
    }

    private void ApplyTheme(AppTheme theme)
    {
        _flyoutWindow?.ApplyTheme(theme);
        _settingsWindow?.ApplyTheme(theme);
        _trayMenu?.ApplyTheme(theme);
        _trayTooltip?.ApplyTheme(theme);
    }

    private void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.OpenFolderFailed(_logger, ex);
        }
    }

    public void Dispose()
    {
        // A show request already queued must not open windows that are shutting down
        _quitting = true;
        _trimTimer?.Stop();
        _shortcuts?.Suspend();
        _trayIcon?.Dispose();
        _trayMenu?.Close();
        _trayTooltip?.Close();
        _settingsWindow?.Close();
        _flyoutWindow?.Shutdown();
        _flyout?.Dispose();
        _manager?.Dispose();
        _notifications?.Dispose();
        _quitWait?.Unregister(null);
        _quitRequest?.Dispose();
        _showWait?.Unregister(null);
        _loggerFactory?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Quit()
    {
        AppLog.Quit(_logger);
        Dispose();
        Exit();
    }
}
