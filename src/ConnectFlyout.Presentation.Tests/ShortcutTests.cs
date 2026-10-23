using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.Shortcuts;

namespace ConnectFlyout.Presentation.Tests;

[TestClass]
public sealed class ShortcutTests
{
    [TestMethod]
    public void EveryDefaultParsesAndMapsToItsMode()
    {
        CollectionAssert.AreEqual(
            new[] { "Win+F10", "Win+F11", "Win+F6", "Win+F9" },
            ShortcutActions.All.Select(ShortcutActions.DefaultFor).ToArray());

        foreach (var action in ShortcutActions.All)
        {
            Assert.IsTrue(Hotkey.TryParse(ShortcutActions.DefaultFor(action), out var hotkey), action.ToString());
            Assert.AreEqual(ShortcutActions.DefaultFor(action), hotkey.ToString());
        }

        Assert.AreEqual(NoiseMode.NoiseCancelling, ShortcutActions.NoiseModeFor(ShortcutAction.NoiseCancelling));
        Assert.AreEqual(NoiseMode.Ambient, ShortcutActions.NoiseModeFor(ShortcutAction.Ambient));
        Assert.AreEqual(NoiseMode.Off, ShortcutActions.NoiseModeFor(ShortcutAction.Off));
    }

    [TestMethod]
    public void F12IsAllowedWithAModifier()
    {
        Assert.IsTrue(Hotkey.TryParse("win+f12", out var hotkey));
        Assert.AreEqual(0x7B, hotkey.Key);
        Assert.IsTrue(HotkeySettings.FromHotkey(hotkey).IsValid());
        Assert.IsFalse(Hotkey.TryParse("F12", out _));
    }

    [TestMethod]
    public void HyperKeyWithSpaceOrDeleteIsAllowed()
    {
        var space = new HotkeySettings(win: true, ctrl: true, alt: true, shift: true, code: 0x20);
        var delete = new HotkeySettings(win: true, ctrl: true, alt: false, shift: false, code: 0x2E);

        Assert.AreEqual("Ctrl+Alt+Shift+Win+Space", space.ToHotkey()?.ToString());
        Assert.AreEqual("Ctrl+Win+Delete", delete.ToHotkey()?.ToString());
        Assert.IsTrue(Hotkey.TryParse("Ctrl+Alt+Shift+Win+Space", out var parsed));
        Assert.AreEqual(0x20, parsed.Key);
    }

    [TestMethod]
    public void ShortcutsUseTheDefaultUntilChangedAndStayClearedOnceCleared()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        Assert.AreEqual("Win+F10", settings.GetShortcut(ShortcutAction.NoiseCancelling));

        settings.SetShortcut(ShortcutAction.NoiseCancelling, "Ctrl+Alt+N");
        Assert.AreEqual("Ctrl+Alt+N", settings.GetShortcut(ShortcutAction.NoiseCancelling));

        settings.SetShortcut(ShortcutAction.NoiseCancelling, "");
        Assert.AreEqual("", settings.GetShortcut(ShortcutAction.NoiseCancelling));
    }
}
