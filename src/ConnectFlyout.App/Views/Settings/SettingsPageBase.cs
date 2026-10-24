using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// What every settings page gets as its navigation parameter: the shared view model, and the
/// shortcuts for pages that show a shortcut picker.
/// </summary>
public sealed record SettingsPageContext(SettingsViewModel ViewModel, ShortcutsContext Shortcuts);

/// <summary>
/// Settings page that receives the shared <see cref="SettingsPageContext"/> as its navigation parameter.
/// </summary>
public partial class SettingsPageBase : Page
{
    public SettingsPageContext Context { get; private set; } = null!;

    public SettingsViewModel ViewModel => Context.ViewModel;

    public ShortcutsContext Shortcuts => Context.Shortcuts;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        Context = (SettingsPageContext)e.Parameter;
        base.OnNavigatedTo(e);
    }
}
