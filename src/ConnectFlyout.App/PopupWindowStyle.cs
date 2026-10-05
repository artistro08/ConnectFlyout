using System.Runtime.InteropServices;

namespace ConnectFlyout.App;

/// <summary>
/// Turns a WinUI window into a plain, frameless popup, the style Battery Flyout's window uses.
/// </summary>
/// <remarks>
/// The presenter's "no border" (<c>SetBorderAndTitleBar(false, false)</c>) still leaves a 3px
/// dialog frame (WS_DLGFRAME) that draws as a white outline, and WinUI puts it back whenever
/// the window shows. A subclass strips the frame bits from every style change after WinUI's
/// own handler has run.
/// </remarks>
internal static class PopupWindowStyle
{
    // Static, so the delegate outlives every native subclass that calls it
    private static readonly NativeMethods.SubclassProc StyleGuard = StripFrame;

    public static void Apply(IntPtr hwnd)
    {
        _ = NativeMethods.SetWindowSubclass(hwnd, StyleGuard, UIntPtr.Zero, UIntPtr.Zero);
        _ = NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWL_STYLE, new IntPtr(NativeMethods.WS_POPUP | NativeMethods.WS_CLIPSIBLINGS | NativeMethods.WS_SYSMENU));
        _ = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        // Square corners: whatever the window shows draws its own
        var corner = NativeMethods.DWMWCP_DONOTROUND;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

    private static IntPtr StripFrame(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        // WinUI's own handler adds the frame back, so it runs first and the frame comes off after
        var result = NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message == NativeMethods.WM_STYLECHANGING && wParam.ToInt64() == NativeMethods.GWL_STYLE)
        {
            var style = Marshal.PtrToStructure<NativeMethods.STYLESTRUCT>(lParam);
            style.styleNew &= ~NativeMethods.WS_FRAME_BITS;
            Marshal.StructureToPtr(style, lParam, false);
        }
        return result;
    }
}
