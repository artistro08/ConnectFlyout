using Microsoft.UI.Xaml;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Paired Sony headphones, a card each with their settings, plus Reorder (opens
/// <see cref="ReorderDevicesPage"/>).
/// </summary>
public sealed partial class DevicesPage : SettingsPageBase
{
    public DevicesPage()
    {
        InitializeComponent();
    }

    private void OnReorderClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ReorderDevicesPage), ViewModel);
}
