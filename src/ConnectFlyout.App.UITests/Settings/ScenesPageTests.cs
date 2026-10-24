using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ConnectFlyout.App.Views;
using ConnectFlyout.App.Views.Settings;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Scenes;

namespace ConnectFlyout.App.UITests.Settings;

[TestClass]
public sealed class ScenesPageTests
{
    private TestRig _rig = null!;

    [TestCleanup]
    public Task Cleanup() => UiHost.RunAsync(async () =>
    {
        await UiHost.ClearAsync();
        _rig?.Dispose();
    });

    private static List<ToggleSwitch> SceneSwitches(DependencyObject page) =>
        [.. VisualTree.Descendants<ToggleSwitch>(page).Where(each => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(each) == "Scene on")];

    [TestMethod]
    public Task SceneCardSwitchesShowWhichSceneTheHeadphonesAreIn() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        var headset = await _rig.ConnectXm6Async();
        var fake = _rig.FakeXm6;
        // Office: Ambient sound at 10 with Focus on voice
        fake.RaiseStateChanged(fake.State with { NoiseControl = new NoiseControlSetting(NoiseMode.Ambient, 10, true) });
        var (_, page) = await Pages.OpenAsync<ScenesPage>(_rig.Context);

        var switches = SceneSwitches(page);
        Assert.AreEqual(3, switches.Count);
        Assert.IsTrue(await UiHost.UntilAsync(() => switches[1].IsOn), "Office should be on.");
        Assert.IsFalse(switches[0].IsOn);
        Assert.IsFalse(switches[2].IsOn);

        fake.RaiseStateChanged(fake.State with { NoiseControl = new NoiseControlSetting(NoiseMode.NoiseCancelling, 0, false) });
        Assert.IsTrue(await UiHost.UntilAsync(() => switches[0].IsOn && !switches[1].IsOn), "Focus should take over.");
        Assert.IsTrue(headset.IsNoiseCancelling);
    });

    [TestMethod]
    public Task SceneCardSwitchAppliesTheScene() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        var headset = await _rig.ConnectXm6Async();
        var fake = _rig.FakeXm6;
        fake.RaiseStateChanged(fake.State with { NoiseControl = new NoiseControlSetting(NoiseMode.NoiseCancelling, 0, false) });
        var (_, page) = await Pages.OpenAsync<ScenesPage>(_rig.Context);
        var switches = SceneSwitches(page);
        Assert.IsTrue(await UiHost.UntilAsync(() => switches[0].IsOn));
        var commands = fake.Commands.Count;

        switches[2].IsOn = true;

        Assert.IsTrue(await UiHost.UntilAsync(() => headset.IsAmbient && headset.AmbientLevel == 20), "Aware was not applied.");
        Assert.IsTrue(await UiHost.UntilAsync(() => switches[2].IsOn && !switches[0].IsOn));
        Assert.IsTrue(fake.Commands.Count > commands);

        // The binding echo (IsOn set to what IsActive already is) must not send anything
        commands = fake.Commands.Count;
        switches[2].IsOn = true;
        await UiHost.IdleAsync();
        Assert.AreEqual(commands, fake.Commands.Count);
    });

    [TestMethod]
    public Task NewScenePageSavesOnSaveAndDiscardsOnCancel() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        var vm = _rig.SettingsVm;
        var announced = 0;
        vm.ScenesChanged += (_, _) => announced++;
        var (frame, scenes) = await Pages.OpenAsync<ScenesPage>(_rig.Context);

        VisualTree.Invoke(VisualTree.Descendants<Button>(scenes).Single(each => each.Content as string == "Add scene"));
        await UiHost.IdleAsync();
        var editorPage = frame.Content as SceneEditorPage;
        Assert.IsNotNull(editorPage);
        await Pages.LoadedAsync(editorPage);
        Assert.IsNotNull(vm.NewScene);

        // The rows bind to the draft: the view model's name reaches the box, the mode shows the ambient rows
        vm.NewScene!.Name = "Gym";
        await UiHost.IdleAsync();
        Assert.AreEqual("Gym", VisualTree.Find<TextBox>(editorPage, "Scene name").Text);
        var mode = VisualTree.Find<ComboBox>(editorPage, "Noise control mode");
        mode.SelectedIndex = (int)NoiseMode.Ambient;
        await UiHost.IdleAsync();
        Assert.IsTrue(vm.NewScene.IsAmbient);
        Assert.AreEqual(Visibility.Visible, VisualTree.Find<Slider>(editorPage, "Ambient sound level").Visibility);
        // A plain card, so the first row starts at the card's own padding, not an icon's
        var firstRow = VisualTree.Descendants<Border>(editorPage).First(each => each.Padding.Bottom == 14);
        Assert.AreEqual(16, firstRow.Padding.Left);

        VisualTree.Invoke(VisualTree.Descendants<Button>(editorPage).Single(each => each.Content as string == "Save"));
        await UiHost.IdleAsync();
        Assert.IsInstanceOfType<ScenesPage>(frame.Content);
        Assert.AreEqual(4, vm.Scenes.Count);
        Assert.AreEqual("Gym", vm.Scenes[3].Name);
        Assert.IsFalse(string.IsNullOrEmpty(_rig.Settings.Scenes[3].Id));
        Assert.AreEqual(1, announced);

        // Cancel leaves no trace
        scenes = (ScenesPage)frame.Content;
        await Pages.LoadedAsync(scenes);
        VisualTree.Invoke(VisualTree.Descendants<Button>(scenes).Single(each => each.Content as string == "Add scene"));
        await UiHost.IdleAsync();
        editorPage = (SceneEditorPage)frame.Content;
        await Pages.LoadedAsync(editorPage);
        vm.NewScene!.Name = "Never";
        VisualTree.Invoke(VisualTree.Descendants<Button>(editorPage).Single(each => each.Content as string == "Cancel"));
        await UiHost.IdleAsync();
        Assert.IsInstanceOfType<ScenesPage>(frame.Content);
        Assert.IsNull(vm.NewScene);
        Assert.AreEqual(4, vm.Scenes.Count);
        Assert.AreEqual(4, _rig.Settings.Scenes.Count);
        Assert.AreEqual(1, announced);
    });

    [TestMethod]
    public Task IconPickerFiltersBySearchAndSelectAppliesTheIcon() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        var vm = _rig.SettingsVm;
        var (_, page) = await Pages.OpenAsync<ScenesPage>(_rig.Context);
        var card = VisualTree.Descendants<SettingsExpander>(page).First();
        await Pages.ExpandAsync(card);

        VisualTree.Invoke(VisualTree.Find<Button>(card, "Change the icon"));
        IconPickerDialog? dialog = null;
        Assert.IsTrue(await UiHost.UntilAsync(() =>
        {
            dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(UiHost.Root.XamlRoot).Select(popup => popup.Child).OfType<IconPickerDialog>().FirstOrDefault();
            return dialog is not null;
        }), "The icon picker did not open.");
        var current = vm.Scenes[0].Glyph;
        var search = (TextBox)dialog!.FindName("SearchBox");
        var grid = (GridView)dialog.FindName("IconGrid");
        var noMatch = (TextBlock)dialog.FindName("NoMatch");
        Assert.AreEqual(SceneIcon.All.Count, grid.Items.Count);

        search.Text = "Moon";
        Assert.IsTrue(await UiHost.UntilAsync(() => grid.Items.Count == SceneIcon.Search("Moon").Count));
        Assert.AreEqual(Visibility.Collapsed, noMatch.Visibility);

        search.Text = "zzz";
        Assert.IsTrue(await UiHost.UntilAsync(() => grid.Items.Count == 0));
        Assert.AreEqual(Visibility.Visible, noMatch.Visibility);
        Assert.AreEqual(current, dialog.SelectedGlyph, "Filtering must keep the current icon as the answer.");

        search.Text = "Walk";
        Assert.IsTrue(await UiHost.UntilAsync(() => grid.Items.Count == 1));
        grid.SelectedIndex = 0;
        await UiHost.IdleAsync();
        VisualTree.Invoke(VisualTree.Descendants<Button>(dialog).Single(each => each.Content as string == "Select"));
        Assert.IsTrue(await UiHost.UntilAsync(() => vm.Scenes[0].Glyph == "\uE805"), "Select did not apply the icon.");
        _rig.LetEditsSave();
        Assert.AreEqual("\uE805", _rig.Settings.Scenes[0].Glyph);
    });

    [TestMethod]
    public Task SceneCardRowsIndentUnderTheTitleAndTheChevronIs16() => UiHost.RunAsync(async () =>
    {
        _rig = new TestRig();
        var (_, page) = await Pages.OpenAsync<ScenesPage>(_rig.Context);
        var card = VisualTree.Descendants<SettingsExpander>(page).First();
        await Pages.ExpandAsync(card);

        var itemStyle = (Style)Application.Current.Resources["SettingsExpanderItemStyle"];
        var rows = VisualTree.Descendants<Border>(card).Where(each => each.Style == itemStyle).ToList();
        Assert.IsTrue(rows.Count >= 6, $"Only {rows.Count} rows realized.");
        foreach (var row in rows)
        {
            Assert.AreEqual(58, row.Padding.Left);
            Assert.AreEqual(44, row.Padding.Right);
        }
        Assert.IsTrue(await UiHost.UntilAsync(() => VisualTree.FindByName<FrameworkElement>(card, "ExpandCollapseChevron") is { Width: 16, Height: 16 }), "The chevron was not resized.");
        Assert.AreEqual(16, VisualTree.FindByName<FrameworkElement>(card, "ExpandCollapseChevron").ActualWidth, 0.5);
    });
}
