using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.App.Views.Settings;

namespace ConnectFlyout.App.UITests.Settings;

[TestClass]
public sealed class ReorderPagesTests
{
    private TestRig _rig = null!;

    [TestCleanup]
    public Task Cleanup() => UiHost.RunAsync(async () =>
    {
        await UiHost.ClearAsync();
        _rig?.Dispose();
    });

    private static ListView DragList(Page page)
    {
        var list = VisualTree.Descendants<ListView>(page).Single();
        Assert.IsTrue(list.CanReorderItems && list.CanDragItems && list.AllowDrop, "The list must be drag-reorderable.");
        return list;
    }

    [TestMethod]
    public Task ReorderHeadphonesListsThemInOrderAndSavesAMove() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        _rig.Source.Report(TestRig.Xm6);
        _rig.Source.Report(TestRig.Xm4);
        var vm = _rig.SettingsVm;
        var (frame, devices) = await Pages.OpenAsync<DevicesPage>(_rig.Context);
        VisualTree.Invoke(VisualTree.Descendants<Button>(devices).Single(each => each.Content as string == "Reorder"));
        await UiHost.IdleAsync();
        var page = (ReorderDevicesPage)frame.Content;
        await Pages.LoadedAsync(page);
        var list = DragList(page);
        Assert.AreSame(vm.Headsets, list.ItemsSource);
        Assert.AreEqual(2, list.Items.Count);

        // What a drop does: the move on the shared list, then the save
        vm.Headsets.Move(1, 0);
        vm.SaveHeadsetOrder();
        await UiHost.IdleAsync();
        CollectionAssert.AreEqual(new[] { "AC:80:0A:00:00:04", "AC:80:0A:00:00:06" }, _rig.Settings.HeadsetOrder.ToList());
        Assert.AreEqual("WH-1000XM4", VisualTree.Descendants<TextBlock>((ListViewItem)list.ContainerFromIndex(0)).Last().Text);

        VisualTree.Invoke(VisualTree.Descendants<Button>(page).Single(each => each.Content as string == "Done"));
        await UiHost.IdleAsync();
        Assert.IsInstanceOfType<DevicesPage>(frame.Content);
    });

    [TestMethod]
    public Task ReorderScenesListsThemInOrderAndSavesAMove() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        var vm = _rig.SettingsVm;
        var (frame, scenes) = await Pages.OpenAsync<ScenesPage>(_rig.Context);
        VisualTree.Invoke(VisualTree.Descendants<Button>(scenes).Single(each => each.Content as string == "Reorder"));
        await UiHost.IdleAsync();
        var page = (ReorderScenesPage)frame.Content;
        await Pages.LoadedAsync(page);
        var list = DragList(page);
        Assert.AreSame(vm.Scenes, list.ItemsSource);

        vm.Scenes.Move(2, 0);
        vm.SaveScenes();
        await UiHost.IdleAsync();
        CollectionAssert.AreEqual(new[] { "Aware", "Focus", "Office" }, _rig.Settings.Scenes.Select(scene => scene.Name).ToList());
        Assert.AreEqual("Aware", VisualTree.Descendants<TextBlock>((ListViewItem)list.ContainerFromIndex(0)).Last().Text);

        VisualTree.Invoke(VisualTree.Descendants<Button>(page).Single(each => each.Content as string == "Done"));
        await UiHost.IdleAsync();
        Assert.IsInstanceOfType<ScenesPage>(frame.Content);
    });
}
