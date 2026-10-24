using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.App.Views;
using ConnectFlyout.App.Views.Settings;

namespace ConnectFlyout.App.UITests.Settings;

[TestClass]
public sealed class DevicesPageTests
{
    private TestRig _rig = null!;

    [TestCleanup]
    public Task Cleanup() => UiHost.RunAsync(async () =>
    {
        await UiHost.ClearAsync();
        _rig?.Dispose();
    });

    [TestMethod]
    public Task DeviceCardRowsIndentPastThePicture() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        await _rig.ConnectXm6Async();
        var (_, page) = await Pages.OpenAsync<DevicesPage>(_rig.Context);
        var card = VisualTree.Descendants<SettingsExpander>(page).First();
        await Pages.ExpandAsync(card);

        var itemStyle = (Style)Application.Current.Resources["DeviceExpanderItemStyle"];
        var rows = VisualTree.Descendants<Border>(card).Where(each => each.Style == itemStyle).ToList();
        Assert.IsTrue(rows.Count >= 3, $"Only {rows.Count} rows realized.");
        foreach (var row in rows)
        {
            Assert.AreEqual(80, row.Padding.Left);
            Assert.AreEqual(44, row.Padding.Right);
        }
        // The row text starts under the header's title (both sit inside the card's 1 px border)
        var title = VisualTree.Descendants<TextBlock>(card).First(each => each.Text == "WF-1000XM6");
        var rowText = VisualTree.Descendants<TextBlock>(card).First(each => each.Text == "Device name");
        var titleLeft = title.TransformToVisual(card).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        var rowLeft = rowText.TransformToVisual(card).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        Assert.AreEqual(titleLeft, rowLeft, 0.5, "The row text should start under the device name in the header.");
    });

    [TestMethod]
    public Task SaveAppliesTheTypedNameToTheCardHeader() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        var headset = await _rig.ConnectXm6Async();
        var (_, page) = await Pages.OpenAsync<DevicesPage>(_rig.Context);
        var card = VisualTree.Descendants<SettingsExpander>(page).First();
        await Pages.ExpandAsync(card);

        var box = VisualTree.Find<TextBox>(card, "Device name");
        box.Text = "Desk buds";
        await UiHost.IdleAsync();
        Assert.AreEqual("Desk buds", headset.PendingName);
        Assert.AreEqual("WF-1000XM6", headset.DeviceName, "Typing alone must not rename.");

        VisualTree.Invoke(VisualTree.Find<Button>(card, "Save the device name"));
        Assert.IsTrue(await UiHost.UntilAsync(() => headset.DeviceName == "Desk buds"));
        Assert.IsTrue(VisualTree.Descendants<TextBlock>(card).Any(each => each.Text == "Desk buds"));
        Assert.AreEqual("Desk buds", _rig.Settings.GetDisplayName(headset.Id));
    });
}
