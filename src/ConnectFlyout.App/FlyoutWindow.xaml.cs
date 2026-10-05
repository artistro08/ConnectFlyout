using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using ConnectFlyout.App.Views;
using ConnectFlyout.Presentation.Placement;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace ConnectFlyout.App;

/// <summary>
/// Borderless, always-on-top, see-through window that holds the tray flyout's acrylic panel.
/// </summary>
/// <remarks>
/// Built the way Battery Flyout builds its flyout. The window never moves: it sits flush on the
/// taskbar edge, as tall as the flyout may get and one margin wider than the panel. Only the
/// panel is visible, and it slides in and out inside the window, so the window's own edge at the
/// taskbar cuts it off and it looks like it comes out from behind the taskbar. Hides when it loses focus, like
/// Windows' own flyouts. The click on the tray icon that takes focus away arrives right after
/// the hide, so a toggle within <see cref="ReopenGuardMilliseconds"/> of hiding is ignored.
/// </remarks>
public sealed partial class FlyoutWindow : Window
{
    private const long ReopenGuardMilliseconds = 300;

    // Asks the placement for the tallest the work area allows; the panel inside is only as tall as its content
    private const double TallestDip = 10_000;

    // Fluent motion: https://learn.microsoft.com/windows/apps/design/motion/timing-and-easing
    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ExitDuration  = TimeSpan.FromMilliseconds(167);

    private readonly FlyoutViewModel _viewModel;
    private readonly FlyoutView _view;
    private readonly IntPtr _hwnd;
    private readonly Visual _panelVisual;
    private readonly Compositor _compositor;
    private long _lastHiddenAt;
    private bool _shuttingDown;
    private bool _isOpen;
    private TaskbarEdge _edge;

    // Current window region in window pixels
    private (int Left, int Top, int Right, int Bottom) _clip;
    private CompositionScopedBatch? _slide;
    private Storyboard? _resize;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        _view = new FlyoutView(viewModel);
        _view.CloseRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        ViewHost.Content = _view;

        _hwnd = WindowNative.GetWindowHandle(this);

        // Window Chrome
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = "ConnectFlyout";

        // Plain frameless popup; the panel inside draws its own rounded corners and stroke
        PopupWindowStyle.Apply(_hwnd);

        // Slide Animation Target
        ElementCompositionPreview.SetIsTranslationEnabled(SlideHost, true);
        _panelVisual = ElementCompositionPreview.GetElementVisual(SlideHost);
        _compositor = _panelVisual.Compositor;

        // Live resize: the panel's height animates to the content's, the region follows the
        // panel. The region updates after every layout pass, because a hidden window lays out
        // only once it's shown again.
        _view.SizeChanged += (_, _) => ResizePanel(animate: _isOpen);
        WindowRoot.LayoutUpdated += (_, _) => ClipToPanel();

        // Behavior
        AppWindow.Closing += (_, e) =>
        {
            if (!_shuttingDown)
            {
                e.Cancel = true;
                HideFlyout();
            }
        };
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && _isOpen)
            {
                HideFlyout();
            }
        };
    }

    /// <summary>
    /// Raised by Esc and by the view's close actions.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Opens the flyout if it isn't already (a notification was clicked).
    /// </summary>
    internal void Open(NativeMethods.RECT? iconRect)
    {
        if (!_isOpen)
        {
            ShowFlyout(iconRect);
        }
    }

    internal void Toggle(NativeMethods.RECT? iconRect)
    {
        if (_isOpen)
        {
            HideFlyout();
            return;
        }
        if (Environment.TickCount64 - _lastHiddenAt < ReopenGuardMilliseconds)
        {
            return;
        }
        ShowFlyout(iconRect);
    }

    /// <summary>
    /// Slides the panel back behind the taskbar, then hides the window.
    /// </summary>
    public void HideFlyout()
    {
        if (!_isOpen)
        {
            return;
        }
        _isOpen = false;
        _lastHiddenAt = Environment.TickCount64;

        // Hand focus back so keys don't go to the closing window
        if (NativeMethods.GetForegroundWindow() == _hwnd)
        {
            NativeMethods.SetForegroundWindow(NativeMethods.FindWindowW("Shell_TrayWnd", null));
        }
        Slide(HiddenOffset(), ExitDuration, new Vector2(1, 0), new Vector2(1, 1));
    }

    public void ApplyTheme(AppTheme theme) => WindowRoot.RequestedTheme = theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>
    /// Lets the window really close when the app quits.
    /// </summary>
    public void Shutdown()
    {
        _shuttingDown = true;
        Close();
    }

    private void ShowFlyout(NativeMethods.RECT? iconRect)
    {
        _isOpen = true;
        _ = _viewModel.OnOpenedAsync();

        // Still sliding out from a close that hasn't finished: turn around from where it is
        if (!AppWindow.IsVisible)
        {
            // Placing the window can resize it, which moves every row; no glides until it's open
            ImplicitMotion.Pause();
            PlaceWindow(iconRect);
            _panelVisual.Properties.InsertVector3("Translation", HiddenOffset());
            AppWindow.Show(true);
        }
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);

        Slide(Vector3.Zero, EnterDuration, new Vector2(0, 0), new Vector2(0, 1));
    }

    // Window = the tallest the flyout may get, one margin wider than the panel, flush on the
    // taskbar edge. The panel sits against the taskbar side and is only as tall as its content,
    // so it grows and shrinks live; the window region keeps the rest see-through to clicks.
    private void PlaceWindow(NativeMethods.RECT? iconRect)
    {
        var (panel, edge, _, scale) = ScreenGeometry.GetFlyoutPlacement(iconRect, TallestDip);
        var margin = (int)Math.Round(FlyoutPlacement.MarginDip * scale);
        _edge = edge;
        SlideHost.VerticalAlignment = edge == TaskbarEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;

        // ponytail: sized twice because moving to a monitor with another DPI rescales the
        // window once it's shown; a WM_DPICHANGED hook would do it in one pass.
        var bounds = new RectInt32(panel.Left - margin, panel.Top - margin, panel.Width + (2 * margin), panel.Height + (2 * margin));
        AppWindow.MoveAndResize(bounds);
        AppWindow.MoveAndResize(bounds);

        WindowRoot.UpdateLayout();
        ResizePanel(animate: false);
        WindowRoot.UpdateLayout();
        _clip = default;
        ClipToPanel();
    }

    // Panel height = content height (plus its border), capped at what the window allows.
    // ponytail: content taller than the cap is cut off rather than scrolled; only matters on
    // screens shorter than the device page (about 600 DIPs).
    private void ResizePanel(bool animate)
    {
        var target = _view.ActualHeight + Panel.BorderThickness.Top + Panel.BorderThickness.Bottom;
        var tallest = WindowRoot.ActualHeight - WindowRoot.Padding.Top - WindowRoot.Padding.Bottom;
        if (tallest > 0)
        {
            target = Math.Min(target, tallest);
        }

        // Start from wherever a running resize got to
        var current = Panel.ActualHeight;
        _resize?.Stop();
        _resize = null;
        if (!animate || current <= 0 || Math.Abs(current - target) < 0.5)
        {
            Panel.Height = target;
            return;
        }
        Panel.Height = current;

        var animation = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = true };
        animation.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = EnterDuration,
            Value = target,
            KeySpline = new KeySpline { ControlPoint1 = new Windows.Foundation.Point(0, 0), ControlPoint2 = new Windows.Foundation.Point(0, 1) },
        });
        Storyboard.SetTarget(animation, Panel);
        Storyboard.SetTargetProperty(animation, "Height");

        var resize = new Storyboard();
        resize.Children.Add(animation);
        resize.Completed += (_, _) =>
        {
            if (_resize == resize)
            {
                Panel.Height = target;
                resize.Stop();
                _resize = null;
            }
        };
        _resize = resize;
        resize.Begin();
    }

    // Far enough to put the whole panel past the window edge at the taskbar, in DIPs
    private Vector3 HiddenOffset()
    {
        var width = (float)(SlideHost.ActualWidth + FlyoutPlacement.MarginDip);
        var height = (float)(SlideHost.ActualHeight + FlyoutPlacement.MarginDip);
        return _edge switch
        {
            TaskbarEdge.Top => new Vector3(0, -height, 0),
            TaskbarEdge.Left => new Vector3(-width, 0, 0),
            TaskbarEdge.Right => new Vector3(width, 0, 0),
            _ => new Vector3(0, height, 0),
        };
    }

    // Window region = the panel plus its margin (room for the shadow), stretched to the taskbar
    // edge so the slide still shows. Everything else doesn't draw and lets clicks through.
    private void ClipToPanel()
    {
        if (WindowRoot.XamlRoot is not { } root)
        {
            return;
        }
        var scale = root.RasterizationScale;
        var top = SlideHost.ActualOffset.Y - FlyoutPlacement.MarginDip;
        var bottom = SlideHost.ActualOffset.Y + SlideHost.ActualHeight + FlyoutPlacement.MarginDip;
        if (_edge == TaskbarEdge.Top)
        {
            top = 0;
        }
        else
        {
            bottom = WindowRoot.ActualHeight;
        }

        var clip = (0, (int)Math.Floor(top * scale), (int)Math.Ceiling(WindowRoot.ActualWidth * scale), (int)Math.Ceiling(bottom * scale));
        if (clip == _clip)
        {
            return;
        }
        _clip = clip;

        // The window owns the region after this call.
        var region = NativeMethods.CreateRectRgn(clip.Item1, clip.Item2, clip.Item3, clip.Item4);
        _ = NativeMethods.SetWindowRgn(_hwnd, region, true);
    }

    // Animates the panel's translation from wherever it is now; hides the window once a
    // close finishes, unless it was reopened in the meantime
    private void Slide(Vector3 to, TimeSpan duration, Vector2 control1, Vector2 control2)
    {
        var animation = _compositor.CreateVector3KeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, to, _compositor.CreateCubicBezierEasingFunction(control1, control2));
        animation.Duration = duration;

        var slide = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _panelVisual.StartAnimation("Translation", animation);
        slide.End();
        _slide = slide;

        slide.Completed += (_, _) =>
        {
            if (_slide != slide)
            {
                return;
            }
            if (_isOpen)
            {
                ImplicitMotion.Resume();
                return;
            }
            AppWindow.Hide();
        };
    }
}
