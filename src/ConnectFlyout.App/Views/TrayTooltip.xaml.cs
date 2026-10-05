using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views;

/// <summary>
/// Content of the tray icon's hover tooltip: the connected headsets with their batteries,
/// drawn like a Windows 11 tooltip that fades in and out.
/// </summary>
public sealed partial class TrayTooltip : UserControl
{
    private readonly Storyboard _fadeIn;
    private readonly Storyboard _fadeOut;

    public TrayTooltip()
    {
        InitializeComponent();
        _fadeIn = (Storyboard)Resources["FadeIn"];
        _fadeOut = (Storyboard)Resources["FadeOut"];
        _fadeOut.Completed += (_, _) => FadedOut?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The fade out finished; the host window can hide.
    /// </summary>
    public event EventHandler? FadedOut;

    /// <summary>
    /// Space around the panel for its shadow, in DIPs; the panel's edge sits this far inside
    /// the host window.
    /// </summary>
    public Thickness ShadowMargin { get; } = new(16);

    /// <summary>
    /// Shows these headsets, or "No headphones connected" when there are none.
    /// </summary>
    public void SetHeadsets(IReadOnlyList<HeadsetViewModel> headsets)
    {
        HeadsetList.ItemsSource = headsets;
        EmptyText.Visibility = headsets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Fades in, cutting short a fade out still running.
    /// </summary>
    public void FadeIn()
    {
        _fadeOut.Stop();
        _fadeIn.Begin();
    }

    /// <summary>
    /// Fades out from wherever the fade in got to, then raises <see cref="FadedOut"/>.
    /// </summary>
    public void FadeOut()
    {
        // Stop the fade in (so it can't finish at full opacity later) and carry on from its
        // current point
        var current = Root.Opacity;
        _fadeIn.Stop();
        ((DoubleAnimation)_fadeOut.Children[0]).From = current;
        _fadeOut.Begin();
    }
}
