using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.Tests.Fakes;

namespace ConnectFlyout.Presentation.Tests;

[TestClass]
public sealed class HeadsetManagerTests
{
    private static readonly BluetoothDeviceInfo Xm6 = new("device-xm6", "WF-1000XM6", "ac:80:0a:00:00:06", true);

    private readonly FakeDeviceSource _source = new();
    private readonly FakeTimeProvider _time = new();
    private readonly Dictionary<string, FakeHeadset> _created = [];
    private readonly AppSettings _settings = new(new InMemorySettingsStore());
    private readonly List<string> _createdModels = [];
    private HeadsetManager _manager = null!;

    [TestInitialize]
    public void CreateManager()
    {
        _manager = new HeadsetManager(
            _source,
            name =>
            {
                _createdModels.Add(name);
                return _created[name] = new FakeHeadset(name);
            },
            _settings,
            _time,
            NullLogger<HeadsetManager>.Instance);
        _manager.Start();
    }

    [TestCleanup]
    public void DisposeManager() => _manager.Dispose();

    [TestMethod]
    public void StartsWatchingWindowsDevices() => Assert.IsTrue(_source.IsWatching);

    [TestMethod]
    public async Task ConnectedSonyHeadsetIsAddedAndConnected()
    {
        ManagedHeadset? added = null;
        _manager.HeadsetAdded += (_, headset) => added = headset;

        _source.Report(Xm6);

        Assert.IsNotNull(added);
        Assert.AreEqual("AC:80:0A:00:00:06", added.Id);
        Assert.IsTrue(await TestWait.UntilAsync(() => added.ConnectionState == HeadsetConnectionState.Connected));
        Assert.AreEqual(1, _created["WF-1000XM6"].ConnectAttempts);
    }

    [TestMethod]
    public async Task ControlLinkWaitsForTheHeadsetToSettleAfterWindowsConnects()
    {
        // Right after Windows connects, a headset still setting up audio drops a control link
        _manager.Dispose();
        _manager = new HeadsetManager(_source, name => _created[name] = new FakeHeadset(name), _settings, _time, NullLogger<HeadsetManager>.Instance)
        {
            WindowsConnectSettle = TimeSpan.FromSeconds(1.5),
        };
        _manager.Start();

        _source.Report(Xm6);
        await Task.Delay(50);
        Assert.AreEqual(0, _created["WF-1000XM6"].ConnectAttempts);

        _time.Advance(TimeSpan.FromSeconds(1.5));

        Assert.IsTrue(await TestWait.UntilAsync(() => _created["WF-1000XM6"].ConnectAttempts == 1));
    }

    [TestMethod]
    public void OtherBrandsAreIgnored()
    {
        _source.Report(new BluetoothDeviceInfo("device-buds", "Galaxy Buds2", "00:11:22:33:44:55", true));

        Assert.AreEqual(0, _manager.Headsets.Count);
    }

    [TestMethod]
    public async Task PairedButDisconnectedHeadsetIsListedWithoutConnecting()
    {
        _source.Report(Xm6 with { IsConnected = false });
        await Task.Delay(50);

        Assert.AreEqual(1, _manager.Headsets.Count);
        Assert.IsFalse(_manager.Headsets[0].IsWindowsConnected);
        Assert.AreEqual(HeadsetConnectionState.Disconnected, _manager.Headsets[0].ConnectionState);
        Assert.AreEqual(0, _created["WF-1000XM6"].ConnectAttempts);
    }

    [TestMethod]
    public async Task WindowsDisconnectKeepsHeadsetAndDropsTheLink()
    {
        var removed = false;
        _manager.HeadsetRemoved += (_, _) => removed = true;
        _source.Report(Xm6);
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));

        _source.Report(Xm6 with { IsConnected = false });

        Assert.IsFalse(removed);
        Assert.AreEqual(1, _manager.Headsets.Count);
        Assert.IsFalse(_manager.Headsets[0].IsWindowsConnected);
        Assert.AreEqual(HeadsetConnectionState.Disconnected, _manager.Headsets[0].ConnectionState);
        Assert.AreEqual(1, _created["WF-1000XM6"].DisconnectCalls);
        Assert.IsFalse(_created["WF-1000XM6"].IsDisposed);
    }

    [TestMethod]
    public async Task WindowsReconnectingAKnownHeadsetConnectsAgain()
    {
        _source.Report(Xm6);
        var headset = _created["WF-1000XM6"];
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 1));
        _source.Report(Xm6 with { IsConnected = false });

        _source.Report(Xm6);

        Assert.IsTrue(_manager.Headsets[0].IsWindowsConnected);
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 2));
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
    }

    [TestMethod]
    public async Task ReleaseDropsTheLinkAndStaysOffUntilReconnect()
    {
        _source.Report(Xm6);
        var headset = _created["WF-1000XM6"];
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));

        _manager.Release("AC:80:0A:00:00:06");

        Assert.AreEqual(HeadsetConnectionState.Disconnected, _manager.Headsets[0].ConnectionState);
        Assert.AreEqual(1, headset.DisconnectCalls);
        Assert.IsFalse(_settings.IsAutoConnectEnabled("AC:80:0A:00:00:06"), "release isn't remembered");

        // Windows dropping and reconnecting the headset doesn't bring the app back
        _source.Report(Xm6 with { IsConnected = false });
        _source.Report(Xm6);
        _time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50);
        Assert.AreEqual(1, headset.ConnectAttempts);
    }

    [TestMethod]
    public async Task ReconnectingAReleasedHeadsetTurnsAutoConnectBackOn()
    {
        _settings.SetAutoConnectEnabled("AC:80:0A:00:00:06", false);
        _source.Report(Xm6);

        _manager.Reconnect("AC:80:0A:00:00:06");

        Assert.IsTrue(_settings.IsAutoConnectEnabled("AC:80:0A:00:00:06"));
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
    }

    [TestMethod]
    public async Task ReconnectWhileWindowsHasItDisconnectedTriesOnce()
    {
        _source.Report(Xm6 with { IsConnected = false });
        var headset = _created["WF-1000XM6"];
        headset.ConnectFailures.Enqueue(new COMException("timeout", HeadsetErrorMessages.TimeoutHResult));

        _manager.Reconnect("AC:80:0A:00:00:06");
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 1));
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        Assert.AreEqual(1, headset.ConnectAttempts);
        Assert.AreEqual(HeadsetConnectionState.Disconnected, _manager.Headsets[0].ConnectionState);
    }

    [TestMethod]
    public void UnpairingRemovesAndDisposesHeadset()
    {
        ManagedHeadset? removed = null;
        _manager.HeadsetRemoved += (_, headset) => removed = headset;
        _source.Report(Xm6);

        _source.Remove(Xm6.Id);

        Assert.IsNotNull(removed);
        Assert.AreEqual(0, _manager.Headsets.Count);
        Assert.IsTrue(_created["WF-1000XM6"].IsDisposed);
    }

    [TestMethod]
    public async Task FailedConnectRetriesAtOneTwoFiveThenThirtySeconds()
    {
        _source.Report(Xm6);
        var headset = _created["WF-1000XM6"];
        // First attempt already ran and succeeded; make the next ones fail.
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 1));
        for (var i = 0; i < 5; i++)
        {
            headset.ConnectFailures.Enqueue(new COMException("timeout", HeadsetErrorMessages.TimeoutHResult));
        }
        _manager.Reconnect("AC:80:0A:00:00:06");
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 2));

        await AdvanceAndExpectAttempts(headset, TimeSpan.FromMilliseconds(999), 2);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromMilliseconds(1), 3);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(2), 4);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(5), 5);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(29), 5);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(1), 6);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(30), 7);

        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
    }

    [TestMethod]
    public async Task DroppedLinkReconnectsAfterOneSecond()
    {
        _source.Report(Xm6);
        var headset = _created["WF-1000XM6"];
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));

        headset.RaiseDisconnected();

        Assert.AreEqual(HeadsetConnectionState.Disconnected, _manager.Headsets[0].ConnectionState);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromMilliseconds(900), 1);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromMilliseconds(100), 2);
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
    }

    [TestMethod]
    public async Task LinkDroppingDuringAConnectKeepsBackingOff()
    {
        _source.Report(Xm6);
        var headset = _created["WF-1000XM6"];
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
        headset.RaiseDisconnected();

        // The headset accepts the link, then drops it before the connect finishes
        var gate = new TaskCompletionSource();
        headset.ConnectGate = gate;
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(1), 2);
        headset.RaiseDisconnected();
        gate.SetException(new COMException("Transport not connected", unchecked((int)0x8007048F)));

        // Next try waits 2 s, not another 1 s
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(1), 2);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(1), 3);
    }

    [TestMethod]
    public async Task ConnectThatSucceedsAfterTheLinkDroppedStillCountsAsFailed()
    {
        _source.Report(Xm6);
        var headset = _created["WF-1000XM6"];
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
        headset.RaiseDisconnected();

        var gate = new TaskCompletionSource();
        headset.ConnectGate = gate;
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(1), 2);
        headset.RaiseDisconnected();
        gate.SetResult();

        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Disconnected));
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(1), 2);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(1), 3);
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
    }

    [TestMethod]
    public async Task WindowsDisconnectStopsRetrying()
    {
        _source.Report(Xm6);
        var headset = _created["WF-1000XM6"];
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 1));
        headset.ConnectFailures.Enqueue(new COMException("timeout", HeadsetErrorMessages.TimeoutHResult));
        headset.ConnectFailures.Enqueue(new COMException("timeout", HeadsetErrorMessages.TimeoutHResult));
        _manager.Reconnect("AC:80:0A:00:00:06");
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 2));

        _source.Report(Xm6 with { IsConnected = false });
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        Assert.AreEqual(2, headset.ConnectAttempts);
    }

    [TestMethod]
    public async Task AutoConnectOffListsButDoesNotConnect()
    {
        _settings.SetAutoConnectEnabled("AC:80:0A:00:00:06", false);

        _source.Report(Xm6);
        await Task.Delay(50);

        Assert.AreEqual(1, _manager.Headsets.Count);
        Assert.AreEqual(0, _created["WF-1000XM6"].ConnectAttempts);
        Assert.AreEqual(HeadsetConnectionState.Disconnected, _manager.Headsets[0].ConnectionState);
    }

    [TestMethod]
    public async Task TurningAutoConnectOffDisconnects()
    {
        _source.Report(Xm6);
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));

        _settings.SetAutoConnectEnabled("AC:80:0A:00:00:06", false);
        _manager.ApplyAutoConnect("AC:80:0A:00:00:06");

        Assert.AreEqual(HeadsetConnectionState.Disconnected, _manager.Headsets[0].ConnectionState);
        Assert.AreEqual(1, _created["WF-1000XM6"].DisconnectCalls);
    }

    [TestMethod]
    public async Task ReconnectWhileConnectingWaitsForInFlightAttempt()
    {
        var gate = new TaskCompletionSource();
        _source.Report(Xm6);
        var headset = _created["WF-1000XM6"];
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 1));
        headset.ConnectGate = gate;
        _manager.Reconnect("AC:80:0A:00:00:06");
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 2));

        _manager.Reconnect("AC:80:0A:00:00:06");
        await Task.Delay(100);
        Assert.AreEqual(2, headset.ConnectAttempts, "a second connect started while the first was in flight");

        gate.SetResult();
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 3));
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
    }

    [TestMethod]
    public async Task RenamedHeadsetIsStillTrackedUnderItsModel()
    {
        _source.Report(Xm6);
        _source.Report(Xm6 with { IsConnected = false });

        _source.Report(Xm6 with { Name = "Devin's buds" });

        Assert.AreEqual(1, _manager.Headsets.Count);
        Assert.AreEqual("Devin's buds", _manager.Headsets[0].Name);
        Assert.AreEqual("WF-1000XM6", _createdModels[^1]);
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
    }

    private async Task AdvanceAndExpectAttempts(FakeHeadset headset, TimeSpan by, int expected)
    {
        _time.Advance(by);
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == expected), $"expected {expected} attempts, saw {headset.ConnectAttempts}");
        await Task.Delay(20);
        Assert.AreEqual(expected, headset.ConnectAttempts);
    }
}
