using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Settings page that receives the shared <see cref="SettingsViewModel"/> as its navigation parameter.
/// </summary>
public partial class SettingsPageBase : Page
{
    public SettingsViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        ViewModel = (SettingsViewModel)e.Parameter;
        base.OnNavigatedTo(e);
    }
}
