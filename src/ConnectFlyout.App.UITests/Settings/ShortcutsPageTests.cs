using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.App.Views.Settings;
using ConnectFlyout.Presentation.Shortcuts;

namespace ConnectFlyout.App.UITests.Settings;

[TestClass]
public sealed class ShortcutsPageTests
{
    private const string NoTrayIcon = "Shortcuts are not available because the tray icon did not start.";

    private TestRig _rig = null!;

    [TestCleanup]
    public Task Cleanup() => UiHost.RunAsync(async () =>
    {
        await UiHost.ClearAsync();
        _rig?.Dispose();
    });

    // Each row is a card followed by its warning bar; the card's title sits in the grid's middle column
    private static List<string> Titles(Panel rows) =>
        [.. rows.Children.OfType<Border>().Select(card => ((Grid)card.Child).Children.OfType<TextBlock>().Single(each => Grid.GetColumn(each) == 1).Text)];

    [TestMethod]
    public Task BuildsOneRowPerActionAndPerSceneWithTheOtherDevicesName() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig { OtherDeviceName = "Pixel 9" };
        var (_, page) = await Pages.OpenAsync<ShortcutsPage>(_rig.Shortcuts);
        var rows = VisualTree.FindByName<StackPanel>(page, "Rows");
        var sceneRows = VisualTree.FindByName<StackPanel>(page, "SceneRows");

        Assert.AreEqual(ShortcutActions.All.Count * 2, rows.Children.Count);
        Assert.AreEqual(3 * 2, sceneRows.Children.Count);
        Assert.AreEqual(Visibility.Collapsed, VisualTree.FindByName<TextBlock>(page, "NoScenes").Visibility);
        var expected = ShortcutActions.All.Select(action => action == ShortcutAction.PlaybackOther ? "Play on Pixel 9" : ShortcutActions.NameFor(action)).ToList();
        CollectionAssert.AreEqual(expected, Titles(rows));
        CollectionAssert.AreEqual(new[] { "Focus", "Office", "Aware" }, Titles(sceneRows));

        // Every picker carries its row's name, and says why nothing can be registered here
        foreach (var control in VisualTree.Descendants<ShortcutControl>(page))
        {
            Assert.IsFalse(string.IsNullOrEmpty(control.ButtonName));
            Assert.AreEqual(NoTrayIcon, control.Tooltip);
        }
        foreach (var warning in VisualTree.Descendants<InfoBar>(page))
        {
            Assert.AreEqual(NoTrayIcon, warning.Message);
            Assert.IsTrue(warning.IsOpen);
        }
    });

    [TestMethod]
    public Task FallsBackToAGenericNameAndSaysWhenThereAreNoScenes() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig(settings => settings.Scenes = []);
        var (frame, page) = await Pages.OpenAsync<ShortcutsPage>(_rig.Shortcuts);

        CollectionAssert.Contains(Titles(VisualTree.FindByName<StackPanel>(page, "Rows")), "Play on another device");
        Assert.AreEqual(0, VisualTree.FindByName<StackPanel>(page, "SceneRows").Children.Count);
        Assert.AreEqual(Visibility.Visible, VisualTree.FindByName<TextBlock>(page, "NoScenes").Visibility);

        // Leaving the page detaches the rows without complaint
        await Pages.NavigateAsync<AppPage>(frame, _rig.Context);
        Assert.IsInstanceOfType<AppPage>(frame.Content);
    });
}
