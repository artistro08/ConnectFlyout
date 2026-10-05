using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views;

/// <summary>
/// Battery levels for one headset in a single row: L, R and case badges for earbuds, or one
/// level for headphones. Used in the flyout header and the headphone picker.
/// </summary>
public sealed partial class BatteryRow : UserControl
{
    public static readonly DependencyProperty HeadsetProperty =
        DependencyProperty.Register(nameof(Headset), typeof(HeadsetViewModel), typeof(BatteryRow), new PropertyMetadata(null));

    public BatteryRow()
    {
        InitializeComponent();
    }

    public HeadsetViewModel? Headset
    {
        get => (HeadsetViewModel?)GetValue(HeadsetProperty);
        set => SetValue(HeadsetProperty, value);
    }
}
