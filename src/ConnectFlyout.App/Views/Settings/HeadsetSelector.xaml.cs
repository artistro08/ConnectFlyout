using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Headset picker shared by the Sound, Noise &amp; scenes and System pages.
/// </summary>
public sealed partial class HeadsetSelector : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(SettingsViewModel),
        typeof(HeadsetSelector),
        new PropertyMetadata(null, (sender, _) => ((HeadsetSelector)sender).Bindings.Update()));

    public HeadsetSelector()
    {
        InitializeComponent();
    }

    public SettingsViewModel? ViewModel
    {
        get => (SettingsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
