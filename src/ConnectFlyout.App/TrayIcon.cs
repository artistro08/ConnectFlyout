using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ConnectFlyout.App;

/// <summary>
/// The notification-area icon.
/// </summary>
/// <remarks>
/// Owns a hidden top-level window that receives the icon's callback messages, the
/// "TaskbarCreated" broadcast (Explorer restarted, so the icon is added again) and theme
/// changes (the icon switches between its light- and dark-taskbar versions). Uses
/// NOTIFYICON_VERSION_4, so a click or Enter arrives as NIN_SELECT/NIN_KEYSELECT and a
/// right-click as WM_CONTEXTMENU with the anchor point in wParam. The menu itself is WinUI
/// (<see cref="TrayMenuWindow"/>). The standard text tooltip is off (no NIF_SHOWTIP), so
/// hovering sends NIN_POPUPOPEN/NIN_POPUPCLOSE for the app's own tooltip
/// (<see cref="TrayTooltipWindow"/>); the text stays set for screen readers.
/// </remarks>
internal sealed class TrayIcon : IDisposable
{
    private const string WindowClassName = "ConnectFlyoutTrayWindow";
    private const uint IconId = 1;

    // Fixed identity, so Windows keeps the icon's taskbar placement across updates. Each update
    // installs to a new folder; Windows only carries settings over when the GUID stays the same
    // and both exes are Authenticode-signed by the same publisher (Build-Package.ps1 signs it).
    // https://learn.microsoft.com/windows/win32/api/shellapi/ns-shellapi-notifyicondataw#troubleshooting
    // The classic install gets its own: a GUID belongs to one exe location, and both installs
    // can sit side by side.
    private static readonly Guid IconGuid = AppIdentity.IsPackaged
        ? new("3F6C2E1B-8D4A-4C7E-9B21-5A0E7D3C9F64")
        : new("8B1D4F72-6A3C-4E95-B0D7-2C9E5F1A7B38");
    private const uint CallbackMessage = NativeMethods.WM_APP + 1;

    private readonly string _tooltip;
    private readonly NativeMethods.WndProc _windowProc;
    private readonly IntPtr _hwnd;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _icon;
    private bool _disposed;

    public TrayIcon(string tooltip)
    {
        _tooltip = tooltip;
        _windowProc = WindowProc;

        var windowClass = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = WindowClassName,
        };
        if (NativeMethods.RegisterClassExW(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _hwnd = NativeMethods.CreateWindowExW(0, WindowClassName, tooltip, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, windowClass.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
    }

    /// <summary>
    /// Left click or Enter.
    /// </summary>
    public event EventHandler? Invoked;

    /// <summary>
    /// Right click or the menu key, with the anchor point in screen pixels.
    /// </summary>
    public event EventHandler<(int X, int Y)>? ContextMenuRequested;

    /// <summary>
    /// The pointer rested on the icon; time to show the hover tooltip.
    /// </summary>
    public event EventHandler? PopupOpening;

    /// <summary>
    /// The pointer left the icon; the hover tooltip should go.
    /// </summary>
    public event EventHandler? PopupClosing;

    public void Show()
    {
        LoadIcon();

        var data = CreateData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP);
        // Fails when Explorer isn't up yet at sign-in; TaskbarCreated adds the icon later.
        if (!NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref data))
        {
            return;
        }

        data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_SETVERSION, ref data);
    }

    /// <summary>
    /// Screen rectangle of the icon, or null when the shell can't report it.
    /// </summary>
    public NativeMethods.RECT? GetIconRect()
    {
        var identifier = new NativeMethods.NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = IconId,
            guidItem = IconGuid,
        };
        return NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var rect) == 0 ? rect : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        var data = CreateData(0);
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref data);
        NativeMethods.DestroyWindow(_hwnd);
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
        }
    }

    private static bool TaskbarUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    private NativeMethods.NOTIFYICONDATAW CreateData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags | NativeMethods.NIF_GUID,
        guidItem = IconGuid,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private void LoadIcon()
    {
        // Light taskbar gets the dark glyph and the other way around. The .ico holds a
        // separately drawn image per size, so Windows never has to shrink a large one.
        var fileName = TaskbarUsesLightTheme() ? "TrayLight.ico" : "TrayDark.ico";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        var size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, NativeMethods.GetDpiForSystem());

        var icon = NativeMethods.LoadImageW(IntPtr.Zero, path, NativeMethods.IMAGE_ICON, size, size, NativeMethods.LR_LOADFROMFILE);
        if (icon == IntPtr.Zero)
        {
            return;
        }
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
        }
        _icon = icon;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case NativeMethods.NIN_SELECT:
                case NativeMethods.NIN_KEYSELECT:
                    Invoked?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WM_CONTEXTMENU:
                    var x = (short)(wParam.ToInt64() & 0xFFFF);
                    var y = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    ContextMenuRequested?.Invoke(this, (x, y));
                    break;
                case NativeMethods.NIN_POPUPOPEN:
                    PopupOpening?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.NIN_POPUPCLOSE:
                    PopupClosing?.Invoke(this, EventArgs.Empty);
                    break;
            }
            return IntPtr.Zero;
        }

        if (message == _taskbarCreatedMessage)
        {
            Show();
            return IntPtr.Zero;
        }

        if (message == NativeMethods.WM_SETTINGCHANGE)
        {
            LoadIcon();
            var data = CreateData(NativeMethods.NIF_ICON);
            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref data);
        }

        return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
    }
}
