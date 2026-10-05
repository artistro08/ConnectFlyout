using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ConnectFlyout.App;

/// <summary>
/// Makes the whole window see-through, so only the elements drawn on it show.
/// </summary>
/// <remarks>
/// A window's backdrop slot takes any composition brush; a fully transparent one leaves the
/// window clear. The flyout draws its own acrylic panel with <c>SystemBackdropElement</c>, the
/// same way Battery Flyout does. Same technique as WinUIEx's TransparentTintBackdrop:
/// https://github.com/dotMorten/WinUIEx/blob/main/src/WinUIEx/TransparentTintBackdrop.cs
/// </remarks>
public sealed partial class TransparentBackdrop : SystemBackdrop
{
    private Windows.UI.Composition.Compositor? _compositor;

    // Kept in a field so the delegate outlives the native subclass that calls it
    private NativeMethods.SubclassProc? _subclass;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        // The backdrop slot uses the system compositor, which needs a system dispatcher queue
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().EnsureSystemDispatcherQueue();
        _compositor ??= new Windows.UI.Composition.Compositor();
        connectedTarget.SystemBackdrop = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        // Tell DWM to blend the window with what's behind it instead of filling it black:
        // blur-behind with a region that covers nothing turns on alpha without any blur
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId);
        var margins = new NativeMethods.MARGINS();
        _ = NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var nowhere = NativeMethods.CreateRectRgn(-2, -2, -1, -1);
        var blurBehind = new NativeMethods.DWM_BLURBEHIND
        {
            dwFlags  = NativeMethods.DWM_BB_ENABLE | NativeMethods.DWM_BB_BLURREGION,
            fEnable  = true,
            hRgnBlur = nowhere,
        };
        _ = NativeMethods.DwmEnableBlurBehindWindow(hwnd, ref blurBehind);
        _ = NativeMethods.DeleteObject(nowhere);

        // No 1px outline around the window, and erase to black (see-through here) instead of
        // the default white, which flashes for a frame when the window first shows
        var noBorder = NativeMethods.DWMWA_COLOR_NONE;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
        _subclass ??= EraseToBlack;
        _ = NativeMethods.SetWindowSubclass(hwnd, _subclass, UIntPtr.Zero, UIntPtr.Zero);
    }

    private static IntPtr EraseToBlack(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message != NativeMethods.WM_ERASEBKGND || !NativeMethods.GetClientRect(hwnd, out var rect))
        {
            return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
        }
        _ = NativeMethods.FillRect(wParam, ref rect, NativeMethods.GetStockObject(NativeMethods.BLACK_BRUSH));
        return 1;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        var brush = disconnectedTarget.SystemBackdrop;
        disconnectedTarget.SystemBackdrop = null;
        brush?.Dispose();

        base.OnTargetDisconnected(disconnectedTarget);
    }
}
