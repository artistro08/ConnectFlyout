using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.App.Views.Settings;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App;

/// <summary>
/// Settings window with a left menu. One page per menu item, all sharing one view model.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Devices"] = typeof(DevicesPage),
        ["Sound"] = typeof(SoundPage),
        ["NoiseScenes"] = typeof(NoiseScenesPage),
        ["System"] = typeof(SystemPage),
        ["General"] = typeof(AppPage),
        ["About"] = typeof(AboutPage),
    };

    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 720));

        // Opening size is also the smallest it can get
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = AppWindow.Size.Width;
            presenter.PreferredMinimumHeight = AppWindow.Size.Height;
        }
        // Taskbar and title bar icon; SetIcon only takes .ico files (a PNG leaves the blank default)
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        Navigation.SelectedItem = Navigation.MenuItems[0];
        _ = _viewModel.LoadAsync();
    }

    public void ApplyTheme(AppTheme theme) => RootGrid.RequestedTheme = theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private void OnPaneToggleRequested(TitleBar sender, object args) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page))
        {
            ContentFrame.Navigate(page, _viewModel);
        }
    }
}
