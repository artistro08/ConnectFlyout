using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views;

/// <summary>
/// Warning bar shown at the top of the settings pages while a headset is disconnected, with a
/// Reconnect button while Windows has the headset.
/// </summary>
public sealed partial class DisconnectedBar : UserControl
{
    public static readonly DependencyProperty HeadsetProperty =
        DependencyProperty.Register(nameof(Headset), typeof(HeadsetViewModel), typeof(DisconnectedBar), new PropertyMetadata(null));

    public DisconnectedBar()
    {
        InitializeComponent();
    }

    public HeadsetViewModel? Headset
    {
        get => (HeadsetViewModel?)GetValue(HeadsetProperty);
        set => SetValue(HeadsetProperty, value);
    }
}
