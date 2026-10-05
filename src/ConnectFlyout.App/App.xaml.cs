using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using ConnectFlyout.Core;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Navigation;
using ConnectFlyout.Presentation.Notifications;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.ViewModels;

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
    private SettingsViewModel? _settingsViewModel;
    private FlyoutWindow? _flyoutWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;
    private TrayMenuWindow? _trayMenu;
    private TrayTooltipWindow? _trayTooltip;

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
            new WindowsBluetoothAudio(),
            time);
        _flyout.SettingsRequested += (_, _) => ShowSettings();
        _flyout.QuitRequested += (_, _) => Quit();

        _settingsViewModel = new SettingsViewModel(
            _flyout,
            _settings,
            levelSwitch,
            AppIdentity.IsPackaged ? new StartupTaskService() : new RegistryStartupService(Environment.ProcessPath!),
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
        _trayMenu = new TrayMenuWindow();
        _trayIcon.Invoked += (_, _) => _flyoutWindow.Toggle(_trayIcon.GetIconRect());
        _trayIcon.ContextMenuRequested += (_, point) =>
        {
            _flyoutWindow.HideFlyout();
            var (x, y, edge, _, _) = ScreenGeometry.GetMenuAnchor(point.X, point.Y);
            _trayMenu.ShowAt(x, y, edge, _flyout.Headsets);
        };
        _trayMenu.SettingsRequested += (_, _) => ShowSettings();
        _trayMenu.QuitRequested += (_, _) => Quit();

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
        _trayIcon.Show();

        ApplyTheme(_settings.Theme);
        _manager.Start();
        AppLog.Started(_logger, AppIdentity.Version);
    }

    private static LogLevel ToLogLevel(NativeLogLevel level) => level switch
    {
        NativeLogLevel.Debug => LogLevel.Debug,
        NativeLogLevel.Warning => LogLevel.Warning,
        NativeLogLevel.Error => LogLevel.Error,
        _ => LogLevel.Information,
    };

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
        if (_settingsViewModel is null || _settings is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsViewModel);
            _settingsWindow.ApplyTheme(_settings.Theme);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;

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
        _trayIcon?.Dispose();
        _trayMenu?.Close();
        _trayTooltip?.Close();
        _settingsWindow?.Close();
        _flyoutWindow?.Shutdown();
        _flyout?.Dispose();
        _manager?.Dispose();
        _notifications?.Dispose();
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
