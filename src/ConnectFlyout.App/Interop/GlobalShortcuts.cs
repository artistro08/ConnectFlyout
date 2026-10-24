using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.Shortcuts;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ConnectFlyout.App.Interop;

/// <summary>
/// The noise mode, flyout and scene shortcuts, registered with <c>RegisterHotKey</c> on the tray icon's hidden
/// window, whose <c>WM_HOTKEY</c> comes back through <see cref="OnHotkey"/>.
/// </summary>
/// <remarks>
/// Adapted from Leaf Calendar's GlobalShortcuts. A combination Windows or another app holds can't be taken: it's
/// remembered as taken and logged, and Settings › Shortcuts warns about it and asks for another. <c>MOD_NOREPEAT</c>
/// keeps a held combination from firing over and over. The noise mode and flyout shortcuts use their
/// <see cref="ShortcutAction"/> value as the hotkey ID; scenes get IDs from <see cref="FirstSceneId"/> up, in the
/// order they're saved, and are told apart by the scene's ID. Call on the UI thread.
/// </remarks>
/// <param name="logger">Where a combination that couldn't be registered is noted (the shortcut's key and the Win32 error only).</param>
/// <author>Devin Green (Artistro08)</author>
/// <seealso href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerhotkey"/>
public sealed class GlobalShortcuts(ILogger logger)
{
    // A spare ID for checking whether a combination is free
    private const int ProbeId = 0x7FFF;

    // Scenes' hotkey IDs start here, clear of the ShortcutAction values
    private const int FirstSceneId = 100;

    private const string ScenePrefix = "Scene:";

    // Hotkey ID to the key of what it runs: the action's name, or ScenePrefix plus the scene's ID
    private readonly Dictionary<int, string> _registered = [];
    private readonly HashSet<string> _taken = [];
    private HWND _hwnd;

    /// <summary>A noise mode or flyout shortcut was pressed.</summary>
    public event EventHandler<ShortcutAction>? Pressed;

    /// <summary>A scene's shortcut was pressed; the argument is the scene's ID.</summary>
    public event EventHandler<string>? ScenePressed;

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
            Register((int)action, action.ToString(), settings.GetShortcut(action));
        }

        var id = FirstSceneId;
        foreach (var scene in settings.Scenes)
        {
            if (scene.Id is not null)
            {
                Register(id++, ScenePrefix + scene.Id, settings.GetSceneShortcut(scene.Id));
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lets go of the shortcuts (while the shortcut dialog listens for keys, and on Quit).</summary>
    public void Suspend()
    {
        foreach (var id in _registered.Keys)
        {
            PInvoke.UnregisterHotKey(_hwnd, id);
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
    public bool IsTaken(ShortcutAction action) => _taken.Contains(action.ToString());

    /// <summary>True when the saved combination for the scene is held by Windows or another app.</summary>
    public bool IsSceneTaken(string sceneId) => _taken.Contains(ScenePrefix + sceneId);

    /// <summary>
    /// The shortcut is set and Windows accepted it, so pressing it works.
    /// </summary>
    public bool IsRegistered(ShortcutAction action) => _registered.ContainsValue(action.ToString());

    /// <summary>A <c>WM_HOTKEY</c> arrived with this ID.</summary>
    public void OnHotkey(int id)
    {
        if (Enum.IsDefined((ShortcutAction)id))
        {
            Pressed?.Invoke(this, (ShortcutAction)id);
        }
        else if (_registered.TryGetValue(id, out var key) && key.StartsWith(ScenePrefix, StringComparison.Ordinal))
        {
            ScenePressed?.Invoke(this, key[ScenePrefix.Length..]);
        }
    }

    // An empty (or unreadable) setting means no shortcut
    private void Register(int id, string key, string text)
    {
        if (_hwnd.IsNull || !Hotkey.TryParse(text, out var hotkey))
        {
            return;
        }

        if (PInvoke.RegisterHotKey(_hwnd, id, Modifiers(hotkey), (uint)hotkey.Key))
        {
            _registered[id] = key;
            return;
        }

        // Taken: Settings › Shortcuts shows the warning; the log gets the key and the error code, never the keys pressed
        var error = Marshal.GetLastPInvokeError();
        _taken.Add(key);
        AppLog.ShortcutTaken(logger, key, error);
    }

    private static HOT_KEY_MODIFIERS Modifiers(Hotkey hotkey) => (HOT_KEY_MODIFIERS)(uint)hotkey.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
}
