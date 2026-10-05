using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ConnectFlyout.App.Views;

/// <summary>
/// Square product picture for a headset model, or the headphone icon when the model has none.
/// </summary>
/// <remarks>
/// Pictures come from <see cref="DeviceArt"/>. Used by the flyout's headphone list and the
/// Settings Devices page.
/// </remarks>
public sealed partial class DevicePicture : UserControl
{
    public static readonly DependencyProperty ModelNameProperty =
        DependencyProperty.Register(nameof(ModelName), typeof(string), typeof(DevicePicture), new PropertyMetadata(null, (picture, _) => ((DevicePicture)picture).UpdatePicture()));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(DevicePicture), new PropertyMetadata(48.0, (picture, _) => ((DevicePicture)picture).UpdateSize()));

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly FontIcon _icon = new() { Glyph = "" };

    public DevicePicture()
    {
        // Decorative: the card's text already names the headphones
        AutomationProperties.SetAccessibilityView(_image, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(_icon, AccessibilityView.Raw);

        Content = new Grid { Children = { _image, _icon } };
        IsTabStop = false;
        UpdateSize();
        UpdatePicture();
    }

    /// <summary>
    /// Model name, e.g. "WF-1000XM6", matching the picture's file name in Assets.
    /// </summary>
    public string? ModelName
    {
        get => (string?)GetValue(ModelNameProperty);
        set => SetValue(ModelNameProperty, value);
    }

    /// <summary>
    /// Width and height in DIPs; the fallback icon is sized to match.
    /// </summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    // Icon at the flyout's proportions: 20 px in a 48 px square
    private void UpdateSize()
    {
        Width          = Size;
        Height         = Size;
        _icon.FontSize = Math.Round(Size * 5 / 12);
    }

    // One disk check: no picture means the icon shows instead
    private void UpdatePicture()
    {
        var source = DeviceArt.For(ModelName);

        _image.Source     = source;
        _image.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        _icon.Visibility  = source is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
