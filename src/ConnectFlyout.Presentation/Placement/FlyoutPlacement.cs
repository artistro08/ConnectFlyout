namespace ConnectFlyout.Presentation.Placement;

/// <summary>
/// Screen rectangle in physical pixels. Right and bottom are exclusive.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>
/// Screen edge the taskbar sits on.
/// </summary>
public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
/// Where the flyout goes on the monitor that holds the tray icon.
/// </summary>
/// <remarks>
/// The flyout is always right-aligned. It sits above a bottom taskbar, below a top taskbar,
/// and at the bottom of the work area when the taskbar is on the left or right. It never leaves
/// the work area; when the work area is too short, the flyout gets shorter.
/// </remarks>
public static class FlyoutPlacement
{
    public const int WidthDip = 360;
    public const int HeightDip = 760;
    public const int MarginDip = 12;

    /// <summary>
    /// Finds the taskbar edge by comparing the monitor to its work area. An auto-hidden taskbar
    /// takes no work area, so it's treated as bottom.
    /// </summary>
    public static TaskbarEdge DetectEdge(PixelRect monitor, PixelRect workArea)
    {
        if (workArea.Bottom < monitor.Bottom)
        {
            return TaskbarEdge.Bottom;
        }
        if (workArea.Top > monitor.Top)
        {
            return TaskbarEdge.Top;
        }
        if (workArea.Left > monitor.Left)
        {
            return TaskbarEdge.Left;
        }
        if (workArea.Right < monitor.Right)
        {
            return TaskbarEdge.Right;
        }
        return TaskbarEdge.Bottom;
    }

    /// <param name="monitor">Full bounds of the monitor holding the tray icon.</param>
    /// <param name="workArea">That monitor's work area.</param>
    /// <param name="scale">Monitor DPI divided by 96.</param>
    /// <param name="heightDip">Height the content wants; the flyout fits its content.</param>
    public static PixelRect Calculate(PixelRect monitor, PixelRect workArea, double scale, double heightDip = HeightDip)
    {
        var edge = DetectEdge(monitor, workArea);
        var margin = (int)Math.Round(MarginDip * scale);
        var width = Math.Min((int)Math.Round(WidthDip * scale), workArea.Width - (2 * margin));
        var height = Math.Min((int)Math.Round(heightDip * scale), workArea.Height - (2 * margin));

        var left = workArea.Right - margin - width;
        var top = edge == TaskbarEdge.Top
            ? workArea.Top + margin
            : workArea.Bottom - margin - height;

        return new PixelRect(left, top, left + width, top + height);
    }
}
