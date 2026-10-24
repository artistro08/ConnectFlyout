using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.App.Views;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Scenes;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.UITests.Flyout;

[TestClass]
public sealed class FlyoutViewTests
{
    private const double FlyoutWidth = 360;
    private const double Tolerance = 0.5;

    private TestRig _rig = null!;

    // The fake hands the gate back as soon as it starts waiting on it, so keep our own reference
    private TaskCompletionSource? _connectGate;

    [TestCleanup]
    public Task Cleanup() => UiHost.RunAsync(async () =>
    {
        await UiHost.ClearAsync();
        _rig?.Dispose();
    });

    // Three states the header and footer must hold their shape through: connected, a reload
    // (Refresh), and "App disconnected" (Disconnect app control)
    private async Task<(FlyoutView View, HeadsetViewModel Headset)> OpenConnectedAsync()
    {
        _rig = new TestRig();
        var headset = await _rig.ConnectXm6Async();
        var view = await UiHost.LoadAsync(new FlyoutView(_rig.Flyout), FlyoutWidth);
        return (view, headset);
    }

    private async Task StartReloadAsync(HeadsetViewModel headset)
    {
        _connectGate = new TaskCompletionSource();
        _rig.FakeXm6.ConnectGate = _connectGate;
        _rig.Flyout.ReconnectCommand.Execute(null);
        Assert.IsTrue(await UiHost.UntilAsync(() => headset.IsReloading), "The reload did not start.");
    }

    private async Task FinishReloadAsync(HeadsetViewModel headset)
    {
        _connectGate?.TrySetResult();
        Assert.IsTrue(await UiHost.UntilAsync(() => headset.IsConnected), "The reload did not finish.");
    }

    private static async Task LetGoAsync(HeadsetViewModel headset)
    {
        headset.DisconnectCommand.Execute(null);
        Assert.IsTrue(await UiHost.UntilAsync(() => headset.ShowReconnect), "Disconnect app control did not release the headphones.");
    }

    [TestMethod]
    public Task HeaderKeepsItsHeightAcrossConnectedReloadingAndAppDisconnected() => UiHost.RunAsync(async () =>
    {
        var (view, headset) = await OpenConnectedAsync();
        var header = VisualTree.FindByName<Grid>(view, "Header");
        var connected = header.ActualHeight;
        Assert.IsTrue(connected > 0);

        await StartReloadAsync(headset);
        Assert.AreEqual(connected, header.ActualHeight, Tolerance, "The header moved during a reload.");

        await FinishReloadAsync(headset);
        await LetGoAsync(headset);
        Assert.AreEqual(connected, header.ActualHeight, Tolerance, "The header moved when the app let go.");
    });

    [TestMethod]
    public Task StatusLineAndReconnectLinkSwapWithoutTheAudioLineWhileReloading() => UiHost.RunAsync(async () =>
    {
        var (view, headset) = await OpenConnectedAsync();
        var status = VisualTree.FindByName<TextBlock>(view, "StatusLine");
        var audio = VisualTree.FindByName<TextBlock>(view, "AudioLine");
        var reconnect = VisualTree.FindByName<HyperlinkButton>(view, "ReconnectLink");
        var ring = VisualTree.Descendants<ProgressRing>(view).Single();

        Assert.AreEqual(Visibility.Collapsed, status.Visibility);
        Assert.AreEqual(Visibility.Collapsed, reconnect.Visibility);
        Assert.AreEqual(Visibility.Visible, audio.Visibility);
        StringAssert.Contains(audio.Text, "LDAC");
        Assert.IsFalse(ring.IsActive);

        await StartReloadAsync(headset);
        Assert.AreEqual(Visibility.Collapsed, status.Visibility, "A reload must not swap in the status line.");
        Assert.AreEqual(Visibility.Visible, audio.Visibility);
        Assert.IsTrue(ring.IsActive);

        await FinishReloadAsync(headset);
        await LetGoAsync(headset);
        Assert.AreEqual(Visibility.Visible, reconnect.Visibility);
        Assert.AreEqual(Visibility.Collapsed, status.Visibility);
        Assert.AreEqual(Visibility.Collapsed, audio.Visibility);

        var before = _rig.FakeXm6.ConnectAttempts;
        VisualTree.Invoke(reconnect);
        Assert.IsTrue(await UiHost.UntilAsync(() => _rig.FakeXm6.ConnectAttempts > before && headset.IsConnected), "The Reconnect link did not reconnect.");
    });

    [TestMethod]
    public Task FooterStaysInPlaceAndOnlyEnablesWhileConnected() => UiHost.RunAsync(async () =>
    {
        var (view, headset) = await OpenConnectedAsync();
        var footer = VisualTree.FindByName<Grid>(view, "Footer");
        var refresh = VisualTree.Find<Button>(view, "Refresh");
        var disconnect = VisualTree.Find<Button>(view, "Disconnect app control");
        var reconnect = VisualTree.Find<Button>(view, "Reconnect");
        var settings = VisualTree.Find<Button>(view, "Settings");
        var turnOff = VisualTree.Descendants<Button>(view).Single(each => each.Command == headset.PowerOffCommand);

        void AssertFooter(bool connected)
        {
            Assert.AreEqual(Visibility.Visible, footer.Visibility);
            Assert.AreEqual(52, footer.ActualHeight, Tolerance);
            Assert.AreEqual(connected, refresh.IsEnabled);
            Assert.AreEqual(connected, disconnect.IsEnabled);
            Assert.AreEqual(connected, turnOff.IsEnabled);
            Assert.AreEqual(Visibility.Visible, turnOff.Visibility);
            Assert.IsTrue(settings.IsEnabled);
        }

        AssertFooter(connected: true);
        Assert.AreEqual(Visibility.Collapsed, reconnect.Visibility);

        await StartReloadAsync(headset);
        AssertFooter(connected: false);
        Assert.AreEqual(Visibility.Visible, disconnect.Visibility);

        await FinishReloadAsync(headset);
        await LetGoAsync(headset);
        AssertFooter(connected: false);
        Assert.AreEqual(Visibility.Collapsed, disconnect.Visibility);
        Assert.AreEqual(Visibility.Visible, reconnect.Visibility);
        Assert.IsTrue(reconnect.IsEnabled);
    });

    [TestMethod]
    public Task PlaybackStaysVisibleButDisabledWhileDisconnected() => UiHost.RunAsync(async () =>
    {
        var (view, headset) = await OpenConnectedAsync();
        var fake = _rig.FakeXm6;
        fake.RaiseStateChanged(fake.State with
        {
            PlaybackDevices = [new PlaybackDevice("AA:BB:CC:DD:EE:01", "DESKTOP", true), new PlaybackDevice("AA:BB:CC:DD:EE:02", "Pixel 9", false)],
        });
        await UiHost.IdleAsync();
        var section = VisualTree.FindByName<ContentControl>(view, "PlaybackSection");

        Assert.AreEqual(Visibility.Visible, section.Visibility);
        Assert.IsTrue(section.IsEnabled);
        Assert.IsTrue(await UiHost.UntilAsync(() => VisualTree.TryFind<Button>(section, "Pixel 9") is not null), "The playback buttons did not appear.");

        await LetGoAsync(headset);
        Assert.AreEqual(Visibility.Visible, section.Visibility, "Playback must stay while the app is disconnected.");
        Assert.IsFalse(section.IsEnabled);
    });

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public Task SceneButtonsShareEachRowsWidth(int count) => UiHost.RunAsync(async () =>
    {
        const double spacing = 8;
        List<Scene> scenes = [.. Scene.Defaults, new Scene("Gym", "\uE805", new NoiseControlSetting(NoiseMode.Off, 0, false), Id: "gym")];
        _rig = new TestRig(settings => settings.Scenes = scenes.Take(count).ToList());
        await _rig.ConnectXm6Async();
        var view = await UiHost.LoadAsync(new FlyoutView(_rig.Flyout), FlyoutWidth);
        var repeater = VisualTree.FindByName<ItemsRepeater>(view, "SceneButtons");
        Assert.AreEqual(Visibility.Visible, repeater.Visibility);
        var width = repeater.ActualWidth;
        Assert.IsTrue(width > 0);

        for (var i = 0; i < count; i++)
        {
            var button = repeater.TryGetElement(i) as FrameworkElement;
            Assert.IsNotNull(button, $"Scene button {i} was not realized.");
            var row = i / 3;
            var inRow = Math.Min(3, count - (row * 3));
            var expected = (width - ((inRow - 1) * spacing)) / inRow;
            Assert.AreEqual(expected, button.ActualWidth, Tolerance, $"Scene button {i} of {count}");
            var top = button.TransformToVisual(repeater).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            Assert.AreEqual(row == 0 ? 0 : button.ActualHeight + spacing, top, Tolerance, $"Scene button {i} row");
        }
    });

    [TestMethod]
    public Task ThemeChangesDoNotThrowOnTheFlyoutOrTheTrayWindows() => UiHost.RunAsync(async () =>
    {
        var (view, _) = await OpenConnectedAsync();
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark, ElementTheme.Default })
        {
            UiHost.Root.RequestedTheme = theme;
            await UiHost.IdleAsync();
        }
        Assert.IsTrue(view.IsLoaded);

        // The windows with the clear backdrop crashed on a theme change once; make sure they don't
        var flyoutWindow = new FlyoutWindow(_rig.Flyout);
        var tooltip = new TrayTooltipWindow(_rig.Flyout);
        var menu = new TrayMenuWindow();
        try
        {
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark, AppTheme.System })
            {
                flyoutWindow.ApplyTheme(theme);
                tooltip.ApplyTheme(theme);
                menu.ApplyTheme(theme);
                await UiHost.IdleAsync();
            }
        }
        finally
        {
            menu.Close();
            tooltip.Close();
            flyoutWindow.Shutdown();
        }
    });
}
