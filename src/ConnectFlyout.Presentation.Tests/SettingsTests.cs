using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Navigation;
using ConnectFlyout.Presentation.Scenes;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.Tests;

[TestClass]
public sealed class AppSettingsTests
{
    [TestMethod]
    public void UsesDefaultsWhenNothingIsSaved()
    {
        var settings = new AppSettings(new InMemorySettingsStore());

        Assert.IsNull(settings.RememberedHeadsetId);
        Assert.IsTrue(settings.LowBatteryNotifications);
        Assert.AreEqual(AppTheme.System, settings.Theme);
        Assert.IsFalse(settings.DebugLogging);
        Assert.IsTrue(settings.IsAutoConnectEnabled("AC:80:0A:12:34:56"));
        CollectionAssert.AreEqual(Scene.Defaults.ToList(), settings.Scenes.ToList());
    }

    [TestMethod]
    public void RoundTripsEveryValue()
    {
        var store = new InMemorySettingsStore();
        var settings = new AppSettings(store)
        {
            RememberedHeadsetId = "AC:80:0A:12:34:56",
            LowBatteryNotifications = false,
            Theme = AppTheme.Dark,
            DebugLogging = true,
        };
        settings.SetAutoConnectEnabled("AC:80:0A:12:34:56", false);

        var reloaded = new AppSettings(store);
        Assert.AreEqual("AC:80:0A:12:34:56", reloaded.RememberedHeadsetId);
        Assert.IsFalse(reloaded.LowBatteryNotifications);
        Assert.AreEqual(AppTheme.Dark, reloaded.Theme);
        Assert.IsTrue(reloaded.DebugLogging);
        Assert.IsFalse(reloaded.IsAutoConnectEnabled("AC:80:0A:12:34:56"));
    }

    [TestMethod]
    public void SavedScenesGetTheNewOfficeIcon()
    {
        var settings = new AppSettings(new InMemorySettingsStore())
        {
            Scenes = [new Scene("Office", "", new NoiseControlSetting(NoiseMode.Ambient, 10, true))],
        };

        Assert.AreEqual(Scene.OfficeGlyph, settings.Scenes[0].Glyph);
    }

    [TestMethod]
    public void RoundTripsScenes()
    {
        var store = new InMemorySettingsStore();
        Scene[] scenes = [new Scene("Gym", "", new NoiseControlSetting(NoiseMode.Ambient, 14, true))];
        new AppSettings(store).Scenes = scenes;

        CollectionAssert.AreEqual(scenes, new AppSettings(store).Scenes.ToList());
    }

    [TestMethod]
    public void FallsBackToDefaultScenesWhenSavedJsonIsBroken()
    {
        var store = new InMemorySettingsStore();
        store.SetString("Scenes", "{not json");

        CollectionAssert.AreEqual(Scene.Defaults.ToList(), new AppSettings(store).Scenes.ToList());
    }

    [TestMethod]
    public void ClearingRememberedHeadsetRemovesIt()
    {
        var settings = new AppSettings(new InMemorySettingsStore()) { RememberedHeadsetId = "A" };
        settings.RememberedHeadsetId = null;

        Assert.IsNull(settings.RememberedHeadsetId);
    }
}

[TestClass]
public sealed class FlyoutNavigatorTests
{
    private const string Xm6 = "AC:80:0A:00:00:06";
    private const string Xm4 = "AC:80:0A:00:00:04";

    private readonly AppSettings _settings = new(new InMemorySettingsStore());

    [TestMethod]
    public void NoHeadsetsShowsEmptyPage() =>
        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Empty, null, false), Navigator().Resolve([]));

    [TestMethod]
    public void OneHeadsetGoesStraightToItsPageWithBackToThePicker() =>
        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm6, true), Navigator().Resolve([Up(Xm6)]));

    [TestMethod]
    public void OneDisconnectedHeadsetStillOpensItsPage() =>
        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm6, true), Navigator().Resolve([Down(Xm6)]));

    [TestMethod]
    public void OneConnectedAmongDisconnectedOpensTheConnectedOne() =>
        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm4, true), Navigator().Resolve([Down(Xm6), Up(Xm4)]));

    [TestMethod]
    public void SeveralConnectedWithNothingRememberedShowsPicker() =>
        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Picker, null, false), Navigator().Resolve([Up(Xm6), Up(Xm4)]));

    [TestMethod]
    public void PickingRemembersTheHeadset()
    {
        Navigator().Pick(Xm4);

        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm4, true), Navigator().Resolve([Up(Xm6), Up(Xm4)]));
    }

    [TestMethod]
    public void PickingADisconnectedHeadsetShowsIt()
    {
        var navigator = Navigator();

        navigator.Pick(Xm6, isAvailable: false);

        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm6, true), navigator.Resolve([Down(Xm6), Up(Xm4)]));
    }

    [TestMethod]
    public void RememberedHeadsetDisconnectingFallsBackToTheConnectedOne()
    {
        _settings.RememberedHeadsetId = Xm6;

        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm4, true), Navigator().Resolve([Down(Xm6), Up(Xm4)]));
        Assert.AreEqual(Xm6, _settings.RememberedHeadsetId);
    }

    [TestMethod]
    public void RememberedHeadsetDisconnectingWithSeveralOthersShowsPicker()
    {
        _settings.RememberedHeadsetId = Xm6;

        Assert.AreEqual(FlyoutPageKind.Picker, Navigator().Resolve([Down(Xm6), Up(Xm4), Up("AC:80:0A:00:00:05")]).Kind);
    }

    [TestMethod]
    public void RememberedHeadsetDisconnectedWithNothingElseConnectedStaysOnIt()
    {
        _settings.RememberedHeadsetId = Xm6;

        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm6, true), Navigator().Resolve([Down(Xm6), Down(Xm4)]));
    }

    [TestMethod]
    public void RememberedHeadsetReconnectingReturnsToItsPage()
    {
        _settings.RememberedHeadsetId = Xm6;

        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm6, true), Navigator().Resolve([Up(Xm4), Up(Xm6)]));
    }

    [TestMethod]
    public void BackAlwaysShowsThePickerEvenWithOneHeadset()
    {
        _settings.RememberedHeadsetId = Xm6;
        var navigator = Navigator();

        navigator.Back();

        Assert.IsNull(_settings.RememberedHeadsetId);
        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Picker, null, false), navigator.Resolve([Up(Xm6)]));
    }

    [TestMethod]
    public void PickingAfterBackLeavesThePicker()
    {
        var navigator = Navigator();
        navigator.Back();

        navigator.Pick(Xm6);

        Assert.AreEqual(FlyoutPageKind.Device, navigator.Resolve([Up(Xm6)]).Kind);
    }

    [TestMethod]
    public void UnknownRememberedHeadsetShowsPicker()
    {
        _settings.RememberedHeadsetId = "UNPAIRED";

        Assert.AreEqual(FlyoutPageKind.Picker, Navigator().Resolve([Up(Xm6), Up(Xm4)]).Kind);
    }

    private static HeadsetAvailability Up(string id) => new(id, true);

    private static HeadsetAvailability Down(string id) => new(id, false);

    private FlyoutNavigator Navigator() => new(_settings);
}
