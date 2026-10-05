using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace ConnectFlyout.App.Views;

/// <summary>
/// Fluent plug icon (16 px unless Size says otherwise): plugged together for Reconnect, pulled
/// apart for Disconnect.
/// </summary>
/// <remarks>
/// Paths are Fluent UI System Icons' plug_connected / plug_disconnected (20 regular, MIT),
/// drawn as a <see cref="PathIcon"/> so they take the text color and follow the theme.
/// </remarks>
public sealed partial class PlugIcon : UserControl
{
    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(PlugIcon), new PropertyMetadata(16.0, (icon, e) =>
        {
            var viewbox = (Viewbox)((PlugIcon)icon).Content;
            viewbox.Width = viewbox.Height = (double)e.NewValue;
        }));

    public static readonly DependencyProperty IsConnectedProperty =
        DependencyProperty.Register(nameof(IsConnected), typeof(bool), typeof(PlugIcon), new PropertyMetadata(false, (icon, _) => ((PlugIcon)icon).Update()));

    // Fluent plug_connected_20_regular
    private const string Connected =
        "M17.8536 2.85355C18.0488 2.65829 18.0488 2.34171 17.8536 2.14645C17.6583 1.95118 17.3417 1.95118 17.1464 2.14645L14.4781 4.81475C12.8949 3.57957 10.6024 3.69014 9.14607 5.14646L8.84607 5.44646C8.26421 6.02832 8.26421 6.97171 8.84607 7.55357L12.4461 11.1536C13.0279 11.7354 13.9713 11.7354 14.5532 11.1536L14.8532 10.8536C16.3094 9.39737 16.42 7.10518 15.1852 5.52191L17.8536 2.85355ZM13.8461 10.4465C13.6547 10.6378 13.3445 10.6378 13.1532 10.4465L9.55318 6.84646C9.36184 6.65513 9.36184 6.34491 9.55318 6.15357L9.85318 5.85357C11.0386 4.66812 12.9606 4.66812 14.1461 5.85357C15.3315 7.03902 15.3315 8.96101 14.1461 10.1465L13.8461 10.4465ZM7.55393 8.84643C6.97207 8.26457 6.02869 8.26457 5.44682 8.84643L5.14683 9.14643C3.69063 10.6026 3.57995 12.8948 4.8148 14.4781L2.14645 17.1464C1.95118 17.3417 1.95118 17.6583 2.14645 17.8536C2.34171 18.0488 2.65829 18.0488 2.85355 17.8536L5.52186 15.1852C7.10515 16.4204 9.39761 16.3099 10.8539 14.8535L11.1539 14.5535C11.7358 13.9717 11.7358 13.0283 11.1539 12.4464L7.55393 8.84643ZM6.15393 9.55354C6.34527 9.3622 6.65549 9.3622 6.84682 9.55354L10.4468 13.1535C10.6382 13.3449 10.6382 13.6551 10.4468 13.8464L10.1468 14.1464C8.96137 15.3319 7.03938 15.3319 5.85393 14.1464C4.66848 12.961 4.66848 11.039 5.85393 9.85354L6.15393 9.55354Z";

    // Fluent plug_disconnected_20_regular
    private const string Disconnected =
        "M17.8536 2.14645C18.0488 2.34171 18.0488 2.65829 17.8536 2.85355L16.1855 4.52163C17.2661 5.907 17.3164 7.83521 16.3366 9.27118C16.1966 9.47632 16.0356 9.67141 15.8536 9.85344L15.5536 10.1534L15.5512 10.1558L15.2425 10.4646C14.8324 10.8746 14.1676 10.8746 13.7576 10.4646L9.53534 6.24233C9.1253 5.83229 9.1253 5.16749 9.53534 4.75745L10.1465 4.14634C11.6027 2.69006 13.8951 2.57945 15.4784 3.81451L17.1464 2.14645C17.3417 1.95118 17.6583 1.95118 17.8536 2.14645ZM15.1404 4.8474C14.1027 3.81542 12.5049 3.68797 11.3289 4.46503C11.1609 4.57604 11.0015 4.70551 10.8536 4.85344L10.5536 5.15344C10.3622 5.34478 10.3622 5.655 10.5536 5.84634L14.1536 9.44634C14.3444 9.6372 14.6536 9.63767 14.845 9.44776L14.8465 9.44634L15.1465 9.14634C16.3299 7.96293 16.3319 6.04552 15.1526 4.85958L15.1464 4.85355L15.1404 4.8474ZM9.35355 8.64645C9.54882 8.84171 9.54882 9.15829 9.35355 9.35355L7.70706 11L8.99995 12.2929L10.6464 10.6464C10.8417 10.4512 11.1583 10.4512 11.3536 10.6464C11.5488 10.8417 11.5488 11.1583 11.3536 11.3536L9.70706 13L10.1536 13.4466C10.7354 14.0284 10.7354 14.9718 10.1536 15.5537L9.85357 15.8537C8.39729 17.3099 6.10492 17.4205 4.52163 16.1855L2.85355 17.8536C2.65829 18.0488 2.34171 18.0488 2.14645 17.8536C1.95118 17.6583 1.95118 17.3417 2.14645 17.1464L3.81454 15.4784C2.57958 13.8951 2.69022 11.6028 4.14646 10.1466L4.44646 9.84655C5.02832 9.26469 5.9717 9.26469 6.55356 9.84655L6.99995 10.2929L8.64645 8.64645C8.84171 8.45118 9.15829 8.45118 9.35355 8.64645ZM4.85896 15.1519C6.04484 16.332 7.9628 16.3302 9.14646 15.1466L9.44646 14.8466C9.6378 14.6552 9.6378 14.345 9.44646 14.1537L5.84646 10.5537C5.65512 10.3623 5.3449 10.3623 5.15356 10.5537L4.85357 10.8537C3.66995 12.0373 3.66812 13.9552 4.84808 15.1411C4.84992 15.1428 4.85174 15.1446 4.85355 15.1464C4.85537 15.1483 4.85717 15.1501 4.85896 15.1519Z";

    private readonly PathIcon _icon = new();

    public PlugIcon()
    {
        // The paths sit on a 20 px grid; the Viewbox scales them to Size (16 px by default)
        Content = new Viewbox { Width = 16, Height = 16, Child = _icon };
        IsTabStop = false;
        Update();
    }

    /// <summary>
    /// Icon width and height in DIPs.
    /// </summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public bool IsConnected
    {
        get => (bool)GetValue(IsConnectedProperty);
        set => SetValue(IsConnectedProperty, value);
    }

    private void Update() =>
        _icon.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), IsConnected ? Connected : Disconnected);
}
