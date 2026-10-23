// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// Ported from PowerToys: src/settings-ui/Settings.UI.Library/HotkeySettings.cs (https://github.com/microsoft/PowerToys).
// By way of Leaf Calendar, whose changes are kept: no JSON or conflict properties (the app stores Hotkey text and
// checks conflicts itself), IsValid also requires Ctrl, Alt, or Win and a key Hotkey accepts (Shift+J is a capital J),
// Windows' own Win+L and Alt+F4 are refused, key names come from Hotkey's table instead of the keyboard layout, and
// ToString joins with "+". ConnectFlyout changes: F12, Space and Delete are allowed (see Hotkey).

using System.Globalization;

namespace ConnectFlyout.Presentation.Shortcuts;

/// <summary>
/// A key combination as the shortcut picker tracks it: Win, Ctrl, Alt, Shift, and one key (a Win32 virtual-key code,
/// 0 for none). <see cref="ToHotkey"/> turns a valid one into the <see cref="Hotkey"/> Leaf registers.
/// </summary>
public sealed record HotkeySettings
{
    private const int VkTab = 0x09;

    /// <summary>No keys.</summary>
    public HotkeySettings()
    {
    }

    /// <summary>A combination; <paramref name="code"/> is a Win32 virtual-key code.</summary>
    public HotkeySettings(bool win, bool ctrl, bool alt, bool shift, int code)
    {
        Win = win;
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift;
        Code = code;
    }

    /// <summary>The Windows key is held.</summary>
    public bool Win { get; set; }

    /// <summary>Ctrl is held.</summary>
    public bool Ctrl { get; set; }

    /// <summary>Alt is held.</summary>
    public bool Alt { get; set; }

    /// <summary>Shift is held.</summary>
    public bool Shift { get; set; }

    /// <summary>The key's virtual-key code, 0 for none.</summary>
    public int Code { get; set; }

    /// <summary>The combination of a saved <see cref="Hotkey"/>.</summary>
    public static HotkeySettings FromHotkey(Hotkey hotkey) => new(
        hotkey.Modifiers.HasFlag(HotkeyModifiers.Win),
        hotkey.Modifiers.HasFlag(HotkeyModifiers.Ctrl),
        hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt),
        hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift),
        hotkey.Key);

    /// <summary>The shortcut Leaf registers, or null when the combination isn't <see cref="IsValid"/>.</summary>
    public Hotkey? ToHotkey() => IsValid() && Hotkey.TryCreate(Modifiers, Code, out var hotkey) ? hotkey : null;

    /// <summary>The key caps in PowerToys' order: Win (92) and Shift (16) as key codes so they draw as glyphs, Ctrl and Alt as text, then the key.</summary>
    public List<object> GetKeysList()
    {
        List<object> shortcutList = [];

        if (Win)
        {
            shortcutList.Add(92); // The Windows key or button.
        }

        if (Ctrl)
        {
            shortcutList.Add("Ctrl");
        }

        if (Alt)
        {
            shortcutList.Add("Alt");
        }

        if (Shift)
        {
            shortcutList.Add(16); // The Shift key or button.
        }

        // Arrow keys (37–40) draw as glyphs, so they stay as codes; every other key is its name
        if (Code > 0)
        {
            shortcutList.Add(Code is >= 37 and <= 40 ? Code : KeyName(Code));
        }

        return shortcutList;
    }

    /// <summary>True for a combination the app can register: Ctrl, Alt, or Win (Shift optional) with a letter, a number, F1–F24, Space, or Delete, and not Win+L or Alt+F4.</summary>
    public bool IsValid()
    {
        if (IsAccessibleShortcut())
        {
            return false;
        }

        if (!(Alt || Ctrl || Win) || Code == 0 || !Hotkey.TryCreate(Modifiers, Code, out var hotkey))
        {
            return false;
        }

        return hotkey.ToString() is not ("Win+L" or "Alt+F4");
    }

    /// <summary>
    /// Why the shortcut dialog refuses the combination, as it says it ("Invalid shortcut. It must start with the Windows
    /// key, Ctrl, or Alt."), or null when it's valid or nothing is pressed yet.
    /// </summary>
    public string? InvalidReason()
    {
        if (IsEmpty() || IsValid())
        {
            return null;
        }

        if (!(Alt || Ctrl || Win))
        {
            return "Invalid shortcut. It must start with the Windows key, Ctrl, or Alt.";
        }

        // Windows' own: Win+L and Alt+F4
        if (ToString() is "Win+L" or "Alt+F4")
        {
            return $"Invalid shortcut. Windows keeps {ToString()} for itself.";
        }

        return "Invalid shortcut. It must end with a letter, a number, an F key, Space, or Delete.";
    }

    /// <summary>No modifier and no key.</summary>
    public bool IsEmpty() => !Alt && !Ctrl && !Win && !Shift && Code == 0;

    /// <summary>Tab and Shift+Tab move focus, so they're never a shortcut.</summary>
    public bool IsAccessibleShortcut() =>
        (!Alt && !Ctrl && !Win && Shift && Code == VkTab)
        || (!Alt && !Ctrl && !Win && !Shift && Code == VkTab);

    /// <summary>The key caps' text joined with "+" ("Win+Shift+F7"); empty for no keys.</summary>
    public override string ToString()
    {
        List<string> parts = [];
        if (Win)
        {
            parts.Add("Win");
        }

        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Code > 0)
        {
            parts.Add(KeyName(Code));
        }

        return string.Join("+", parts);
    }

    private HotkeyModifiers Modifiers =>
        (Win ? HotkeyModifiers.Win : 0) | (Ctrl ? HotkeyModifiers.Ctrl : 0) | (Alt ? HotkeyModifiers.Alt : 0) | (Shift ? HotkeyModifiers.Shift : 0);

    // A key's name: Hotkey's for the keys a shortcut may use, a plain name for common others
    private static string KeyName(int key) => Hotkey.KeyName(key) ?? key switch
    {
        0x08 => "Backspace",
        VkTab => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        _ => string.Create(CultureInfo.InvariantCulture, $"Key {key:X2}"),
    };
}
