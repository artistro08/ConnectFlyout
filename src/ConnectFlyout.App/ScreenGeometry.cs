using System.Runtime.InteropServices;
using ConnectFlyout.Presentation.Placement;

namespace ConnectFlyout.App;

/// <summary>
/// Reads the monitor under the tray icon and turns it into a flyout rectangle.
/// </summary>
internal static class ScreenGeometry
{
    /// <summary>
    /// Where the flyout panel goes on the monitor that holds the tray icon.
    /// </summary>
    /// <param name="iconRect">Tray icon bounds, or null to use the cursor position.</param>
    /// <param name="heightDip">Height the panel wants; capped at what the work area allows.</param>
    /// <returns>Panel rectangle, taskbar edge, work area, and monitor DPI divided by 96.</returns>
    public static (PixelRect Rect, TaskbarEdge Edge, PixelRect WorkArea, double Scale) GetFlyoutPlacement(NativeMethods.RECT? iconRect, double heightDip)
    {
        IntPtr monitor;
        if (iconRect is { } rect)
        {
            monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        }
        else
        {
            NativeMethods.GetCursorPos(out var point);
            monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        }

        var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;

        var bounds = ToPixelRect(info.rcMonitor);
        var workArea = ToPixelRect(info.rcWork);
        return (FlyoutPlacement.Calculate(bounds, workArea, scale, heightDip), FlyoutPlacement.DetectEdge(bounds, workArea), workArea, scale);
    }

    /// <summary>
    /// Where the tray menu opens for a click at a screen point: moved off the taskbar to the
    /// work area's edge plus the flyout's margin, so the menu keeps the same gap as the flyout.
    /// </summary>
    /// <returns>Anchor point, taskbar edge, work area, and monitor DPI divided by 96.</returns>
    public static (int X, int Y, TaskbarEdge Edge, PixelRect WorkArea, double Scale) GetMenuAnchor(int x, int y)
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = x, Y = y }, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        var margin = (int)Math.Round(FlyoutPlacement.MarginDip * scale);

        var workArea = ToPixelRect(info.rcWork);
        var edge = FlyoutPlacement.DetectEdge(ToPixelRect(info.rcMonitor), workArea);
        return edge switch
        {
            TaskbarEdge.Top => (x, workArea.Top + margin, edge, workArea, scale),
            TaskbarEdge.Left => (workArea.Left + margin, y, edge, workArea, scale),
            TaskbarEdge.Right => (workArea.Right - margin, y, edge, workArea, scale),
            _ => (x, workArea.Bottom - margin, edge, workArea, scale),
        };
    }

    private static PixelRect ToPixelRect(NativeMethods.RECT rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
