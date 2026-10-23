// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// Ported from PowerToys: src/common/interop/KeyboardHook.cpp (Start, Close, HookProc) and
// src/common/ManagedCommon/HotkeySettingsControlHook.cs (https://github.com/microsoft/PowerToys).
// By way of Leaf Calendar, whose changes are kept: C# through CsWin32 without marshaling, so the hook procedure is a
// static [UnmanagedCallersOnly] function (no delegate for the GC to collect); one instance at a time (there's one
// shortcut dialog); no exception may leave the hook procedure (a failing callback lets the key through).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ConnectFlyout.App.Interop;

/// <summary>A key event from the low-level hook: the message, the virtual-key code, and the sender's extra info.</summary>
internal readonly record struct KeyboardEvent(uint Message, int Key, nuint ExtraInfo, bool Injected);

/// <summary>
/// A low-level keyboard hook (<c>WH_KEYBOARD_LL</c>) for the shortcut picker: while <c>isActive</c> says so, every key
/// that <c>filter</c> doesn't let through goes to the callback and is swallowed, so Windows and other apps never act on
/// it (Win+letter, Alt+Tab). Runs on the thread that started it (the UI thread), while it pumps messages.
/// </summary>
internal static unsafe class KeyboardHook
{
    private static HHOOK s_hook;
    private static Action<KeyboardEvent>? s_callback;
    private static Func<bool>? s_isActive;
    private static Func<KeyboardEvent, bool>? s_filter;
    private static int s_generation;

    /// <summary>Counts Start calls, so an owner closes only the hook it started.</summary>
    public static int Generation => s_generation;

    /// <summary>Installs the hook (replacing one already installed). False when Windows refused it.</summary>
    public static bool Start(Action<KeyboardEvent> callback, Func<bool> isActive, Func<KeyboardEvent, bool> filter)
    {
        Close();
        s_generation++;
        s_callback = callback;
        s_isActive = isActive;
        s_filter = filter;

        // Register Low Level Hook Procedure
        var module = new HINSTANCE(NativeMethods.GetModuleHandleW(null));
        s_hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, &HookProc, module, 0);
        return !s_hook.IsNull;
    }

    /// <summary>Removes the hook (safe to call when none is installed).</summary>
    public static void Close()
    {
        // The handle goes either way (Windows drops a slow low-level hook itself, and unhooking that one fails)
        if (!s_hook.IsNull)
        {
            PInvoke.UnhookWindowsHookEx(s_hook);
            s_hook = default;
        }

        s_callback = null;
        s_isActive = null;
        s_filter = null;
    }

    /// <summary>Removes the hook only if it's still the one from <paramref name="generation"/> (a newer one stays).</summary>
    public static void Close(int generation)
    {
        if (generation == s_generation)
        {
            Close();
        }
    }

    /// <summary>True while Windows reports <paramref name="virtualKey"/> held (<c>GetAsyncKeyState</c>).</summary>
    public static bool IsDown(int virtualKey) => (PInvoke.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT HookProc(int nCode, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (nCode == (int)PInvoke.HC_ACTION && s_callback is { } callback && s_isActive?.Invoke() == true)
            {
                var info = (KBDLLHOOKSTRUCT*)lParam.Value;
                var ev = new KeyboardEvent((uint)wParam.Value, (int)info->vkCode, info->dwExtraInfo, info->flags.HasFlag(KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED));

                // Ignore the keyboard hook if the FilterKeyboardEvent returns false.
                if (s_filter is null || s_filter(ev))
                {
                    callback(ev);
                    return new LRESULT(1);
                }
            }
        }
#pragma warning disable CA1031 // An exception leaving an unmanaged callback ends the process; the key just goes through
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        return PInvoke.CallNextHookEx(default, nCode, wParam, lParam);
    }
}

/// <summary>
/// The shortcut picker's hook (PowerToys' <c>HotkeySettingsControlHook</c>): key-downs and key-ups as virtual-key codes,
/// while <c>isActive</c>, for the keys <c>filter</c> keeps (it gets the key and the sender's extra info).
/// </summary>
internal sealed class HotkeySettingsControlHook : IDisposable
{
    private const int WmKeyDown = 0x100;
    private const int WmKeyUp = 0x101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkEscape = 0x1B;

    private readonly Action<int> _keyDown;
    private readonly Action<int> _keyUp;
    private readonly Func<bool> _isActive;
    private readonly Func<int, nuint, bool> _filterKeyboardEvent;
    private readonly int _generation;
    private bool _disposedValue;

    public HotkeySettingsControlHook(Action<int> keyDown, Action<int> keyUp, Func<bool> isActive, Func<int, nuint, bool> filterAccessibleKeyboardEvents)
    {
        _keyDown = keyDown;
        _keyUp = keyUp;
        _isActive = isActive;
        _filterKeyboardEvent = filterAccessibleKeyboardEvents;
        IsHooked = KeyboardHook.Start(HotkeySettingsHookCallback, IsActive, FilterKeyboardEvents);
        _generation = KeyboardHook.Generation;
    }

    /// <summary>False when Windows refused the hook (the dialog then can't listen for keys).</summary>
    public bool IsHooked { get; }

    private bool IsActive() => _isActive();

    private void HotkeySettingsHookCallback(KeyboardEvent ev)
    {
        // A sent Esc isn't a real press: Raycast's Hyper key sends one when it's let go, which would clear the
        // shortcut just picked. It's still swallowed, so it doesn't close the dialog either.
        if (ev.Key == VkEscape && ev.Injected)
        {
            return;
        }

        switch (ev.Message)
        {
            case WmKeyDown:
            case WmSysKeyDown:
                _keyDown(ev.Key);
                break;
            case WmKeyUp:
            case WmSysKeyUp:
                _keyUp(ev.Key);
                break;
        }
    }

    private bool FilterKeyboardEvents(KeyboardEvent ev) => _filterKeyboardEvent(ev.Key, ev.ExtraInfo);

    public void Dispose()
    {
        if (!_disposedValue)
        {
            // Remove the hook. KeyboardHook is static with one hook at a time: a newer instance's Start already closed this one's,
            // so Dispose closes only the hook this instance started, never a newer one (whatever order they're disposed in)
            KeyboardHook.Close(_generation);
            _disposedValue = true;
        }
    }
}
