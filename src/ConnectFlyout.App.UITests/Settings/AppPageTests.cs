using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.App.Views;
using ConnectFlyout.App.Views.Settings;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.App.UITests.Settings;

[TestClass]
public sealed class AppPageTests
{
    private TestRig _rig = null!;

    [TestCleanup]
    public Task Cleanup() => UiHost.RunAsync(async () =>
    {
        await UiHost.ClearAsync();
        _rig?.Dispose();
    });

    private static ToggleSwitch Switch(DependencyObject root, string name) => VisualTree.Find<ToggleSwitch>(root, name);

    [TestMethod]
    public Task HideTrayExpanderBindsBothWays() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        var vm = _rig.SettingsVm;
        var (_, page) = await Pages.OpenAsync<AppPage>(_rig.Context);
        var expander = VisualTree.FindByName<SettingsExpander>(page, "TrayExpander");
        await Pages.ExpandAsync(expander);
        var hide = Switch(expander, "Hide the tray icon");
        var untilConnected = VisualTree.Descendants<SettingToggleRow>(expander).Single(each => each.Title.StartsWith("Hide the tray icon until", StringComparison.Ordinal));
        Assert.IsFalse(hide.IsOn);
        Assert.IsTrue(untilConnected.IsEnabled);

        hide.IsOn = true;
        await UiHost.IdleAsync();
        Assert.IsTrue(vm.HideTray);
        Assert.IsTrue(_rig.Settings.HideTray);
        Assert.IsFalse(untilConnected.IsEnabled);

        vm.HideTray = false;
        await UiHost.IdleAsync();
        Assert.IsFalse(hide.IsOn);
        Assert.IsTrue(untilConnected.IsEnabled);
    });

    [TestMethod]
    public Task FlyoutControlSwitchesStartOnAndHideTheFlyoutSectionsWhenOff() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        var headset = await _rig.ConnectXm6Async();
        var (_, page) = await Pages.OpenAsync<AppPage>(_rig.Context);
        var expander = VisualTree.FindByName<SettingsExpander>(page, "FlyoutControlsExpander");
        await Pages.ExpandAsync(expander);
        foreach (var name in new[] { "Enable scenes", "Enable playback", "Enable ambient sound controls", "Enable sound controls" })
        {
            Assert.IsTrue(Switch(expander, name).IsOn, name);
        }

        Switch(expander, "Enable scenes").IsOn = false;
        Switch(expander, "Enable playback").IsOn = false;
        Switch(expander, "Enable ambient sound controls").IsOn = false;
        await UiHost.IdleAsync();
        Assert.IsFalse(_rig.Settings.ShowScenes, "scenes");
        Assert.IsFalse(_rig.Settings.ShowPlayback, "playback");
        Assert.IsFalse(_rig.Settings.ShowAmbientControls, "ambient controls");
        Assert.IsFalse(headset.ShowScenes);
        Assert.IsFalse(headset.ShowPlayback);
        Assert.IsFalse(headset.ShowAmbientControls);

        Switch(expander, "Enable scenes").IsOn = true;
        await UiHost.IdleAsync();
        Assert.IsTrue(_rig.Settings.ShowScenes);
        Assert.IsTrue(headset.ShowScenes);
    });

    [TestMethod]
    public Task DebugLoggingExpanderBindsAndOpensTheLogFolder() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        var vm = _rig.SettingsVm;
        var (_, page) = await Pages.OpenAsync<AppPage>(_rig.Context);
        var expander = VisualTree.FindByName<SettingsExpander>(page, "LoggingExpander");
        await Pages.ExpandAsync(expander);

        Switch(expander, "Debug logging").IsOn = true;
        await UiHost.IdleAsync();
        Assert.IsTrue(vm.DebugLogging);
        Assert.IsTrue(_rig.Settings.DebugLogging);
        Assert.IsTrue(_rig.NativeDebug.Count > 0 && _rig.NativeDebug[^1]);
        Assert.IsTrue(VisualTree.Descendants<TextBlock>(expander).Any(each => each.Text == @"C:\Logs"));

        VisualTree.Invoke(VisualTree.Descendants<Button>(expander).Single(each => each.Content as string == "Open log folder"));
        await UiHost.IdleAsync();
        CollectionAssert.AreEqual(new[] { @"C:\Logs" }, _rig.OpenedFolders);
    });

    [TestMethod]
    public Task ThemePickerAppliesTheChoice() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        var (_, page) = await Pages.OpenAsync<AppPage>(_rig.Context);
        var theme = VisualTree.Find<ComboBox>(page, "App theme");

        theme.SelectedIndex = (int)AppTheme.Light;
        await UiHost.IdleAsync();
        theme.SelectedIndex = (int)AppTheme.Dark;
        await UiHost.IdleAsync();
        theme.SelectedIndex = (int)AppTheme.System;
        await UiHost.IdleAsync();

        CollectionAssert.AreEqual(new[] { AppTheme.Light, AppTheme.Dark, AppTheme.System }, _rig.Themes);
        Assert.AreEqual(AppTheme.System, _rig.Settings.Theme);
    });
}
