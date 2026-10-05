using ConnectFlyout.Presentation.Headsets;
using Core = ConnectFlyout.Core;

namespace ConnectFlyout.Presentation.Tests;

/// <summary>
/// Crosses the real WinRT boundary into ConnectFlyout.Core.dll, the same way the app does.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HeadsetClientActivationTests
{
    [TestMethod]
    public void ResolvesWf1000Xm6Profile()
    {
        using var client = new Core.HeadsetClient("WF-1000XM6");

        Assert.AreEqual("WF-1000XM6", client.ModelName);
        Assert.IsTrue(client.IsKnownModel);
        Assert.AreEqual(Core.ProtocolGeneration.V2, client.Protocol);
        Assert.IsTrue(client.Capabilities.DualBattery);
        Assert.IsTrue(client.Capabilities.Dsee);
        Assert.IsTrue(client.Capabilities.DseeExtreme);
        Assert.IsFalse(client.IsConnected);
    }

    [TestMethod]
    public void ResolvesWhXb900nProfile()
    {
        using var client = new Core.HeadsetClient("WH-XB900N");

        Assert.AreEqual("WH-XB900N", client.ModelName);
        Assert.IsTrue(client.IsKnownModel);
        Assert.AreEqual(Core.ProtocolGeneration.V2, client.Protocol);
        Assert.IsTrue(client.Capabilities.NoiseCancelling);
        Assert.IsTrue(client.Capabilities.AmbientSound);
        Assert.IsTrue(client.Capabilities.Equalizer);
        Assert.IsTrue(client.Capabilities.Dsee);
        Assert.IsTrue(client.Capabilities.ConnectionQuality);
        Assert.IsTrue(client.Capabilities.AutoPowerOff);
        Assert.IsFalse(client.Capabilities.SpeakToChat);
        Assert.IsFalse(client.Capabilities.DseeExtreme);
        Assert.IsTrue(client.Capabilities.VoiceGuidance);
        Assert.IsTrue(client.Capabilities.Vpt);
        Assert.IsTrue(client.Capabilities.SoundPosition);
        Assert.IsFalse(client.Capabilities.PowerOff);
        Assert.IsFalse(client.Capabilities.AutoPowerOffWhenRemoved);
    }

    [TestMethod]
    public void UnknownNameKeepsDeviceNameAsModel()
    {
        using var client = new Core.HeadsetClient("Some Headphones");

        Assert.AreEqual("Some Headphones", client.ModelName);
        Assert.IsFalse(client.IsKnownModel);
        Assert.IsFalse(client.Capabilities.Equalizer);
        Assert.IsTrue(client.Capabilities.PowerOff);
        Assert.IsTrue(client.Capabilities.AutoPowerOffWhenRemoved);
    }

    [TestMethod]
    public async Task InvalidAddressFailsWithTransportFailureHResult()
    {
        using var client = new Core.HeadsetClient("WF-1000XM6");

        var exception = await Assert.ThrowsAsync<Exception>(async () => await client.ConnectAsync("not-an-address"));

        Assert.AreEqual(HeadsetErrorMessages.TransportFailureHResult, exception.HResult);
    }

    [TestMethod]
    public async Task CommandWhileDisconnectedFailsWithDisconnectedHResult()
    {
        using var client = new Core.HeadsetClient("WF-1000XM6");

        var exception = await Assert.ThrowsAsync<Exception>(async () => await client.SetDseeAsync(true));

        Assert.AreEqual(HeadsetErrorMessages.DisconnectedHResult, exception.HResult);
    }

    [TestMethod]
    public void ReturnsEqualizerPresets()
    {
        var presets = Core.HeadsetClient.GetEqualizerPresets();

        Assert.IsTrue(presets.Any(preset => preset.Value == 0x16 && preset.Name == "Bass Boost"));
        Assert.IsTrue(presets.Any(preset => preset.Value == 0xa0 && preset.Name == "Manual"));
    }

    [TestMethod]
    public void EarbudAtZeroCountsAsNotConnected()
    {
        // One earbud out: the headset reports 0 for it, and native "main" is the lower side (0 too)
        var state = new Core.HeadsetState { Battery = new Core.BatteryInfo { Main = 0, Left = 0, Right = 80, CaseBattery = 90 } };

        var battery = WinRtHeadset.ToSnapshot(state).Battery;

        Assert.IsNull(battery.Left);
        Assert.AreEqual(80, battery.Right);
        Assert.IsNull(battery.Main);
        Assert.AreEqual(80, battery.Lowest);
    }

    [TestMethod]
    public void Xb900nStateFieldsCrossTheWinRtBoundary()
    {
        var state = new Core.HeadsetState
        {
            ConnectionQuality = 1,
            VoiceGuidance = 0,
            Vpt = 4,
            SoundPosition = 0x12,
        };

        var snapshot = WinRtHeadset.ToSnapshot(state);

        Assert.AreEqual(1, snapshot.ConnectionQuality);
        Assert.AreEqual(0, snapshot.VoiceGuidance);
        Assert.AreEqual(4, snapshot.Vpt);
        Assert.AreEqual(0x12, snapshot.SoundPosition);
    }

    [TestMethod]
    public void SingleBatteryHeadphonesKeepMainLevel()
    {
        var state = new Core.HeadsetState { Battery = new Core.BatteryInfo { Main = 15, Left = -1, Right = -1, CaseBattery = -1 } };

        Assert.AreEqual(15, WinRtHeadset.ToSnapshot(state).Battery.Lowest);
    }

    [TestMethod]
    public void WrapperStartsWithUnknownBattery()
    {
        using var headset = new WinRtHeadset("WF-1000XM6");

        Assert.IsNull(headset.State.Battery.Left);
        Assert.AreEqual(5, headset.State.Equalizer.Bands.Length);
    }
}

/// <summary>
/// Talks to real headphones. Each test is skipped (Inconclusive) unless its address variable
/// is set, e.g. <c>$env:SONY_TEST_XM6_ADDRESS = "AC:80:0A:12:34:56"</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HardwareTests
{
    [TestMethod]
    [TestCategory("Hardware")]
    public async Task Wf1000Xm6RoundTrip() => await RoundTripAsync("WF-1000XM6", "SONY_TEST_XM6_ADDRESS");

    [TestMethod]
    [TestCategory("Hardware")]
    [TestCategory("XM4")]
    public async Task Wh1000Xm4RoundTrip() => await RoundTripAsync("WH-1000XM4", "SONY_TEST_XM4_ADDRESS");

    [TestMethod]
    [TestCategory("Hardware")]
    [TestCategory("XB900N")]
    public async Task WhXb900nRoundTrip() => await RoundTripAsync("WH-XB900N", "SONY_TEST_XB900N_ADDRESS");

    /// <summary>
    /// Native events (state changes and dropped links) must cross into managed code, or the app
    /// never hears that the link dropped and never reconnects.
    /// </summary>
    [TestMethod]
    [TestCategory("Hardware")]
    public async Task Wf1000Xm6EventsReachManagedCode()
    {
        var address = Environment.GetEnvironmentVariable("SONY_TEST_XM6_ADDRESS");
        if (string.IsNullOrWhiteSpace(address))
        {
            Assert.Inconclusive("Set SONY_TEST_XM6_ADDRESS to the WF-1000XM6's Bluetooth address to run this test.");
        }

        using var headset = new WinRtHeadset("WF-1000XM6");
        var changes = 0;
        headset.StateChanged += (_, _) => Interlocked.Increment(ref changes);
        await headset.ConnectAsync(address);
        var original = headset.State.NoiseControl;
        await headset.SetNoiseControlAsync(new NoiseControlSetting(NoiseMode.NoiseCancelling, 0, false));
        await headset.SetNoiseControlAsync(original);
        headset.Disconnect();

        Assert.IsTrue(changes > 0, "StateChanged never reached managed code");
    }

    private static async Task RoundTripAsync(string model, string variable)
    {
        var address = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(address))
        {
            Assert.Inconclusive($"Set {variable} to the {model}'s Bluetooth address to run this test.");
        }

        using var headset = new WinRtHeadset(model);
        await headset.ConnectAsync(address);
        var original = headset.State.NoiseControl;

        Assert.IsNotNull(headset.State.Battery.Lowest ?? headset.State.Battery.Main, "battery was not reported");

        try
        {
            await headset.SetNoiseControlAsync(new NoiseControlSetting(NoiseMode.NoiseCancelling, 0, false));
            Assert.AreEqual(NoiseMode.NoiseCancelling, headset.State.NoiseControl.Mode);

            await headset.SetNoiseControlAsync(new NoiseControlSetting(NoiseMode.Ambient, 8, original.FocusOnVoice));
            Assert.AreEqual(NoiseMode.Ambient, headset.State.NoiseControl.Mode);
            Assert.AreEqual(8, headset.State.NoiseControl.AmbientLevel);
        }
        finally
        {
            await headset.SetNoiseControlAsync(original);
            headset.Disconnect();
        }
    }
}
