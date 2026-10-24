using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.App.Views.Settings;

namespace ConnectFlyout.App.UITests.Settings;

[TestClass]
public sealed class SettingsWindowTests
{
    private TestRig _rig = null!;

    [TestCleanup]
    public Task Cleanup() => UiHost.RunAsync(async () =>
    {
        await UiHost.ClearAsync();
        _rig?.Dispose();
    });

    // Every page through the real window: missing resources, broken bindings and template
    // errors all surface here
    [TestMethod]
    public Task EveryPageOpensInTheRealWindow() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        await _rig.ConnectXm6Async();
        var window = new SettingsWindow(_rig.SettingsVm, _rig.Shortcuts);
        try
        {
            window.Activate();
            var root = (FrameworkElement)window.Content;
            await Pages.LoadedAsync(root);
            var navigation = VisualTree.Descendants<NavigationView>(root).Single();
            var frame = VisualTree.Descendants<Frame>(root).Single();
            var expected = new Dictionary<string, Type>
            {
                ["Devices"] = typeof(DevicesPage),
                ["Scenes"] = typeof(ScenesPage),
                ["Shortcuts"] = typeof(ShortcutsPage),
                ["General"] = typeof(AppPage),
                ["About"] = typeof(AboutPage),
            };
            foreach (var item in navigation.MenuItems.Concat(navigation.FooterMenuItems).OfType<NavigationViewItem>())
            {
                if (item.Tag is not string tag || !expected.TryGetValue(tag, out var pageType))
                {
                    continue;
                }
                navigation.SelectedItem = item;
                await UiHost.IdleAsync();
                Assert.IsInstanceOfType(frame.Content, pageType, tag);
                await Pages.LoadedAsync((FrameworkElement)frame.Content);
            }
        }
        finally
        {
            window.Close();
        }
    });
}
