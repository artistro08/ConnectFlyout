using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.Shortcuts;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ConnectFlyout.App.Interop;

/// <summary>
/// The noise mode shortcuts, registered with <c>RegisterHotKey</c> on the tray icon's hidden window, whose
/// <c>WM_HOTKEY</c> comes back through <see cref="OnHotkey"/>.
/// </summary>
/// <remarks>
/// Adapted from Leaf Calendar's GlobalShortcuts. A combination Windows or another app holds can't be taken: it's
/// remembered as taken and logged, and Settings › Shortcuts warns about it and asks for another. <c>MOD_NOREPEAT</c>
/// keeps a held combination from firing over and over. Call on the UI thread.
/// </remarks>
/// <param name="logger">Where a combination that couldn't be registered is noted (the action and the Win32 error only).</param>
/// <author>Devin Green (Artistro08)</author>
/// <seealso href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerhotkey"/>
public sealed class GlobalShortcuts(ILogger logger)
{
    // A spare ID for checking whether a combination is free
    private const int ProbeId = 0x7FFF;

    private readonly HashSet<ShortcutAction> _registered = [];
    private readonly HashSet<ShortcutAction> _taken = [];
    private HWND _hwnd;

    /// <summary>A shortcut was pressed.</summary>
    public event EventHandler<ShortcutAction>? Pressed;

    /// <summary>Which shortcuts are registered or taken changed (Settings › Shortcuts refreshes).</summary>
    public event EventHandler? Changed;

    /// <summary>Starts listening on the tray icon's window with the saved shortcuts.</summary>
    public void Attach(nint hwnd, AppSettings settings)
    {
        _hwnd = new HWND(hwnd);
        Apply(settings);
    }

    /// <summary>Registers the saved shortcuts again (after a change, or after the shortcut dialog).</summary>
    public void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Suspend();
        _taken.Clear();
        foreach (var action in ShortcutActions.All)
        {
            Register(action, settings.GetShortcut(action));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lets go of the shortcuts (while the shortcut dialog listens for keys, and on Quit).</summary>
    public void Suspend()
    {
        foreach (var action in _registered)
        {
            PInvoke.UnregisterHotKey(_hwnd, (int)action);
        }

        _registered.Clear();
    }

    /// <summary>True when Windows would let the app register <paramref name="hotkey"/> now.</summary>
    public bool IsFree(Hotkey hotkey)
    {
        if (_hwnd.IsNull)
        {
            return false;
        }

        if (!PInvoke.RegisterHotKey(_hwnd, ProbeId, Modifiers(hotkey), (uint)hotkey.Key))
        {
            return false;
        }

        PInvoke.UnregisterHotKey(_hwnd, ProbeId);
        return true;
    }

    /// <summary>False until the tray icon's window exists (without it nothing can be registered).</summary>
    public bool IsAvailable => !_hwnd.IsNull;

    /// <summary>True when the saved combination for <paramref name="action"/> is held by Windows or another app.</summary>
    public bool IsTaken(ShortcutAction action) => _taken.Contains(action);

    /// <summary>
    /// The shortcut is set and Windows accepted it, so pressing it works.
    /// </summary>
    public bool IsRegistered(ShortcutAction action) => _registered.Contains(action);

    /// <summary>A <c>WM_HOTKEY</c> arrived with this ID.</summary>
    public void OnHotkey(int id)
    {
        if (Enum.IsDefined((ShortcutAction)id))
        {
            Pressed?.Invoke(this, (ShortcutAction)id);
        }
    }

    // An empty (or unreadable) setting means no shortcut
    private void Register(ShortcutAction action, string text)
    {
        if (_hwnd.IsNull || !Hotkey.TryParse(text, out var hotkey))
        {
            return;
        }

        if (PInvoke.RegisterHotKey(_hwnd, (int)action, Modifiers(hotkey), (uint)hotkey.Key))
        {
            _registered.Add(action);
            return;
        }

        // Taken: Settings › Shortcuts shows the warning; the log gets the action and the error code, never the keys
        var error = Marshal.GetLastPInvokeError();
        var name  = action.ToString();
        _taken.Add(action);
        AppLog.ShortcutTaken(logger, name, error);
    }

    private static HOT_KEY_MODIFIERS Modifiers(Hotkey hotkey) => (HOT_KEY_MODIFIERS)(uint)hotkey.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
}
