using System.Runtime.InteropServices;
using ConnectFlyout.Presentation.Common;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.Presentation.Tests;

[TestClass]
public sealed class SonyDeviceNameFilterTests
{
    [TestMethod]
    [DataRow("WF-1000XM6")]
    [DataRow("WH-1000XM4")]
    [DataRow("LE_WF-1000XM6")]
    [DataRow("LinkBuds S")]
    [DataRow("Sony ULT WEAR")]
    public void AcceptsSonyNames(string name) => Assert.IsTrue(SonyDeviceNameFilter.IsSony(name));

    [TestMethod]
    [DataRow("Galaxy Buds2 Pro")]
    [DataRow("AirPods Pro")]
    [DataRow("")]
    [DataRow(null)]
    public void RejectsOtherNames(string? name) => Assert.IsFalse(SonyDeviceNameFilter.IsSony(name));
}

[TestClass]
public sealed class HeadsetErrorMessagesTests
{
    [TestMethod]
    public void DescribesTimeout() =>
        Assert.AreEqual("The headphones did not respond. Try again.", HeadsetErrorMessages.Describe(new COMException("", HeadsetErrorMessages.TimeoutHResult)));

    [TestMethod]
    public void DescribesDisconnect() =>
        Assert.AreEqual("The headphones disconnected.", HeadsetErrorMessages.Describe(new COMException("", HeadsetErrorMessages.DisconnectedHResult)));

    [TestMethod]
    public void DescribesTransportFailure() =>
        Assert.AreEqual(
            "Connection failed. Make sure that Bluetooth is on and the headphones are near the PC.",
            HeadsetErrorMessages.Describe(new COMException("", HeadsetErrorMessages.TransportFailureHResult)));

    [TestMethod]
    public void FallsBackForUnknownErrors() =>
        Assert.AreEqual("Communication with the headphones failed.", HeadsetErrorMessages.Describe(new InvalidOperationException()));
}

[TestClass]
public sealed class GlyphsTests
{
    [TestMethod]
    public void PicksBatteryStepByTens()
    {
        Assert.AreEqual("", Glyphs.Battery(5));
        Assert.AreEqual("", Glyphs.Battery(85));
        Assert.AreEqual("", Glyphs.Battery(100));
        Assert.AreEqual(Glyphs.BatteryUnknown, Glyphs.Battery(null));
    }
}

[TestClass]
public sealed class BatteryLevelsTests
{
    [TestMethod]
    public void LowestIgnoresCaseAndUnknownCells()
    {
        Assert.AreEqual(12, new BatteryLevels(null, 12, 40, 5, false).Lowest);
        Assert.AreEqual(60, new BatteryLevels(60, null, null, null, false).Lowest);
        Assert.IsNull(BatteryLevels.Unknown.Lowest);
    }
}
