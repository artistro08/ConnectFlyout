using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ConnectFlyout.App.Interop;
using ConnectFlyout.App.Views.Settings;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Navigation;
using ConnectFlyout.Presentation.Notifications;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.Tests;
using ConnectFlyout.Presentation.Tests.Fakes;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.UITests;

/// <summary>
/// The app's view models on the Presentation tests' fakes: no hardware, no settings file,
/// fake time. Built on the UI thread so property changes reach the bindings there.
/// </summary>
internal sealed class TestRig : IDisposable
{
    public static readonly BluetoothDeviceInfo Xm6 = new("device-xm6", "WF-1000XM6", "ac:80:0a:00:00:06", true);
    public static readonly BluetoothDeviceInfo Xm4 = new("device-xm4", "WH-1000XM4", "ac:80:0a:00:00:04", true);

    public TestRig(Action<AppSettings>? configure = null)
    {
        Settings = new AppSettings(new InMemorySettingsStore());
        configure?.Invoke(Settings);
        Manager = new HeadsetManager(Source, name => Fakes[name] = new FakeHeadset(name, name == "WH-1000XM4" ? FakeHeadset.Xm4Features : null), Settings, Time, NullLogger<HeadsetManager>.Instance);
        var monitor = new LowBatteryMonitor(Settings, new FakeNotificationService());
        Flyout = new FlyoutViewModel(
            Manager,
            new FlyoutNavigator(Settings),
            managed => new HeadsetViewModel(managed, Settings, monitor, Time, NullLogger.Instance),
            Audio,
            Time,
            Settings);
        SettingsVm = new SettingsViewModel(Flyout, Settings, new LogLevelSwitch(), new FakeStartupTaskService(), Themes.Add, NativeDebug.Add, OpenedFolders.Add, @"C:\Logs", NullLogger.Instance, Time);
        // Unattached: nothing is registered with Windows, and the rows say so
        Shortcuts = new ShortcutsContext(new GlobalShortcuts(NullLogger.Instance), Settings, () => OtherDeviceName);
        Context = new SettingsPageContext(SettingsVm, Shortcuts);
        Manager.Start();
    }

    public AppSettings Settings { get; }

    public FakeTimeProvider Time { get; } = new();

    public FakeDeviceSource Source { get; } = new();

    public FakeBluetoothAudio Audio { get; } = new();

    public Dictionary<string, FakeHeadset> Fakes { get; } = [];

    public HeadsetManager Manager { get; }

    public FlyoutViewModel Flyout { get; }

    public SettingsViewModel SettingsVm { get; }

    public ShortcutsContext Shortcuts { get; }

    public SettingsPageContext Context { get; }

    public List<AppTheme> Themes { get; } = [];

    public List<bool> NativeDebug { get; } = [];

    public List<string> OpenedFolders { get; } = [];

    public string? OtherDeviceName { get; set; }

    /// <summary>
    /// Reports the WF-1000XM6 as paired and connected in Windows and waits for the app's link.
    /// </summary>
    public async Task<HeadsetViewModel> ConnectXm6Async()
    {
        Source.Report(Xm6);
        var headset = Flyout.Headsets.Single(each => each.DeviceName == "WF-1000XM6");
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.IsConnected), "The fake headset did not connect.");
        return headset;
    }

    public FakeHeadset FakeXm6 => Fakes["WF-1000XM6"];

    /// <summary>
    /// Edits save a moment after the last one.
    /// </summary>
    public void LetEditsSave() => Time.Advance(TimeSpan.FromSeconds(1));

    public void Dispose()
    {
        Flyout.Dispose();
        Manager.Dispose();
    }
}
