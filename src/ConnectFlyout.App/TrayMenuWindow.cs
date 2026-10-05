using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using ConnectFlyout.Presentation.Placement;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.ViewModels;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace ConnectFlyout.App;

/// <summary>
/// Hosts the tray icon's right-click menu as a WinUI <see cref="MenuFlyout"/>.
/// </summary>
/// <remarks>
/// A MenuFlyout needs a XAML root, so a 1 × 1 frameless, transparent window sits at the click point and
/// the menu opens outside its bounds (<c>ShouldConstrainToRootBounds = false</c>), away from
/// the taskbar. The window hides again when the menu closes or focus moves elsewhere.
/// </remarks>
internal sealed partial class TrayMenuWindow : Window
{
    private readonly Grid _root = new();
    private readonly MenuFlyout _menu = new() { ShouldConstrainToRootBounds = false };
    private readonly IntPtr _hwnd;
    private FlyoutShowOptions? _pendingShow;

    private readonly UISettings _uiSettings = new();

    // Segoe Fluent Icons' headphones (E7F6) with the cup cut away for its checkmark (E73E), on
    // the same 16 px grid as the menu's font icons
    private const string HeadphonesPath =
        "M15,11.86L15,12.55C15,12.88 14.93,13.2 14.8,13.49 14.67,13.79 14.49,14.05 14.27,14.27 14.05,14.49 13.79,14.67 13.49,14.8 13.2,14.93 12.88,15 12.55,15L11.86,15 12.95,13.91 13.08,13.88C13.26,13.8 13.42,13.7 13.56,13.56 13.7,13.42 13.8,13.26 13.88,13.08L13.91,12.95 15,11.86z M2,10L2,12.5C2,12.7 2.04,12.9 2.12,13.08 2.2,13.26 2.3,13.42 2.44,13.56 2.58,13.7 2.74,13.8 2.92,13.88 3.1,13.96 3.3,14 3.5,14L5,14 5,10 2,10z M10.5,9L12.66,9 11.66,10 11,10 11,10.66 10,11.66 10,9.5C10,9.36 10.05,9.25 10.15,9.15 10.25,9.05 10.36,9 10.5,9z M8,1C8.64,1 9.26,1.08 9.86,1.25 10.45,1.42 11.01,1.65 11.53,1.96 12.05,2.26 12.52,2.63 12.95,3.05 13.37,3.48 13.74,3.95 14.04,4.47 14.35,4.99 14.58,5.55 14.75,6.14L14.89,7.17 14.88,7.17 14.43,7.36 14.05,7.62 13.94,7.72 13.53,5.67C13.21,4.94 12.78,4.31 12.24,3.76 11.69,3.22 11.06,2.79 10.33,2.47 9.61,2.16 8.83,2 8,2 7.17,2 6.39,2.16 5.67,2.47 4.94,2.79 4.31,3.22 3.76,3.76 3.22,4.31 2.79,4.94 2.47,5.67 2.16,6.39 2,7.17 2,8L2,9 5.5,9C5.64,9 5.75,9.05 5.85,9.15 5.95,9.25 6,9.36 6,9.5L6,10.41 5.84,10.48 5.19,11.13 5.01,11.58 5.01,12.5 5.19,12.96 5.45,13.34 6,13.89 6,14.5C6,14.64 5.95,14.75 5.85,14.85 5.75,14.95 5.64,15 5.5,15L3.45,15C3.12,15 2.8,14.93 2.51,14.8 2.21,14.67 1.95,14.49 1.73,14.27 1.51,14.05 1.33,13.79 1.2,13.49 1.07,13.2 1,12.88 1,12.55L1,8C1,7.35 1.08,6.73 1.25,6.14 1.42,5.54 1.65,4.98 1.96,4.46 2.26,3.95 2.63,3.48 3.05,3.05 3.48,2.63 3.95,2.26 4.46,1.96 4.98,1.65 5.54,1.42 6.14,1.25 6.73,1.08 7.35,1 8,1z";

    private const string CheckmarkPath =
        "M15.35,8.27L15.8,8.46 15.99,8.92 15.8,9.37 9.94,15.23 9.49,15.42 9.04,15.23 6.3,12.49 6.11,12.04 6.3,11.59 6.75,11.4 7.2,11.59 7.2,11.59 9.49,13.87 14.9,8.46 14.9,8.46 14.9,8.46 14.9,8.46 15.35,8.27z";

    public TrayMenuWindow()
    {
        Content = _root;
        _hwnd = WindowNative.GetWindowHandle(this);

        _menu.Closed += (_, _) => AppWindow?.Hide();

        // Window Chrome
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        // Invisible host: frameless and see-through, so only the menu shows
        PopupWindowStyle.Apply(_hwnd);
        SystemBackdrop = new TransparentBackdrop();

        // Behavior
        _root.Loaded += (_, _) => ShowPendingMenu();
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                _menu.Hide();
            }
        };
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? QuitRequested;

    /// <summary>
    /// Opens the menu at a screen point (physical pixels), growing away from the taskbar, with
    /// a Connect submenu listing the paired headsets (ones Windows already has are disabled).
    /// </summary>
    public void ShowAt(int x, int y, TaskbarEdge edge, IReadOnlyList<HeadsetViewModel> headsets)
    {
        BuildItems(headsets);

        AppWindow.MoveAndResize(new RectInt32(x, y, 1, 1));
        AppWindow.Show(true);
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);

        _pendingShow = new FlyoutShowOptions
        {
            Position = new Point(0, 0),
            Placement = edge switch
            {
                TaskbarEdge.Top => FlyoutPlacementMode.BottomEdgeAlignedRight,
                TaskbarEdge.Left => FlyoutPlacementMode.RightEdgeAlignedBottom,
                TaskbarEdge.Right => FlyoutPlacementMode.LeftEdgeAlignedBottom,
                _ => FlyoutPlacementMode.TopEdgeAlignedRight,
            },
        };
        if (_root.IsLoaded)
        {
            ShowPendingMenu();
        }
    }

    public void ApplyTheme(AppTheme theme) => _root.RequestedTheme = theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    // Connect submenu (when anything's paired), then Settings and Quit
    private void BuildItems(IReadOnlyList<HeadsetViewModel> headsets)
    {
        _menu.Items.Clear();
        if (headsets.Count > 0)
        {
            var connect = new MenuFlyoutSubItem
            {
                Text = "Connect",
                Icon = new FontIcon { Glyph = "" },
                Padding = (Thickness)Application.Current.Resources["MenuFlyoutItemThemePaddingNarrow"],
            };
            foreach (var headset in headsets)
            {
                // Connected headsets are listed disabled, with a checkmark on the headphones
                IconElement icon = headset.ShowConnect ? new FontIcon { Glyph = "" } : ConnectedHeadphonesIcon();
                var item = CreateItem(headset.DeviceName, icon, () => headset.ConnectCommand.Execute(null));
                item.IsEnabled = headset.ShowConnect;
                connect.Items.Add(item);
            }
            _menu.Items.Add(connect);
            _menu.Items.Add(new MenuFlyoutSeparator());
        }
        _menu.Items.Add(CreateItem("Settings", new FontIcon { Glyph = "" }, () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new MenuFlyoutSeparator());
        _menu.Items.Add(CreateItem("Quit", new FontIcon { Glyph = "" }, () => QuitRequested?.Invoke(this, EventArgs.Empty)));
    }

    private static MenuFlyoutItem CreateItem(string text, IconElement icon, Action onClick)
    {
        // Always the mouse-sized padding. WinUI picks touch padding (taller rows) when it can't
        // tell how the menu was opened, which is often, since the host window never gets input.
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = icon,
            Padding = (Thickness)Application.Current.Resources["MenuFlyoutItemThemePaddingNarrow"],
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    // Headphones in the disabled text color with the checkmark in the system accent color. Built
    // as an SVG each time the menu opens, since a font or path icon only takes one color.
    private ImageIcon ConnectedHeadphonesIcon()
    {
        var dark = _root.ActualTheme == ElementTheme.Dark;

        // TextFillColorDisabled and AccentFillColorDefault, as WinUI picks them per theme
        var headphones = dark ? Color.FromArgb(0x5d, 0xff, 0xff, 0xff) : Color.FromArgb(0x5c, 0x00, 0x00, 0x00);
        var checkmark  = _uiSettings.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.AccentDark1);

        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16">
                <path fill="{Hex(headphones)}" fill-opacity="{headphones.A / 255.0:0.###}" d="{HeadphonesPath}" />
                <path fill="{Hex(checkmark)}" d="{CheckmarkPath}" />
            </svg>
            """;
        // Rasterized at the screen's scale once shown; at 16 px it gets stretched and looks jagged
        var source = new SvgImageSource();
        var icon = new ImageIcon { Source = source };
        icon.Loaded += (_, _) =>
        {
            var pixels = 16 * (icon.XamlRoot?.RasterizationScale ?? 1);
            source.RasterizePixelWidth  = pixels;
            source.RasterizePixelHeight = pixels;
            _ = LoadSvgAsync(source, svg);
        };
        return icon;
    }

    private static string Hex(Color color) => $"#{color.R:x2}{color.G:x2}{color.B:x2}";

    private static async Task LoadSvgAsync(SvgImageSource source, string svg)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteString(svg);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        await source.SetSourceAsync(stream);
    }

    private void ShowPendingMenu()
    {
        if (_pendingShow is not { } options)
        {
            return;
        }
        _pendingShow = null;
        _menu.ShowAt(_root, options);
    }
}
