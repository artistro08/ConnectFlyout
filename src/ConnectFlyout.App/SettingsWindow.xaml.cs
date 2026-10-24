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
        ["Scenes"] = typeof(ScenesPage),
        ["Shortcuts"] = typeof(ShortcutsPage),
        ["General"] = typeof(AppPage),
        ["About"] = typeof(AboutPage),
    };

    private readonly SettingsViewModel _viewModel;
    private readonly ShortcutsContext _shortcuts;
    private readonly SettingsPageContext _context;

    public SettingsWindow(SettingsViewModel viewModel, ShortcutsContext shortcuts)
    {
        _viewModel = viewModel;
        _shortcuts = shortcuts;
        _context = new SettingsPageContext(viewModel, shortcuts);
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));

        // Opens roomy, but can shrink to fit a 768 px laptop screen with its taskbar
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1000;
            presenter.PreferredMinimumHeight = 660;
        }
        // Taskbar and title bar icon; SetIcon only takes .ico files (a PNG leaves the blank default)
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        Navigation.SelectedItem = Navigation.MenuItems[0];
        _ = _viewModel.LoadAsync();

        // A shortcut dialog open as the window closes would keep its keyboard hook (it swallows every
        // key), and a scene edit made a moment ago is still waiting to save
        Closed += (_, _) =>
        {
            ShortcutRow.CloseAll();
            _viewModel.FlushScenes();
        };
    }

    /// <summary>
    /// The Quit menu item was clicked.
    /// </summary>
    public event EventHandler? QuitRequested;

    /// <summary>
    /// Shows the Quit menu item, for while the tray icon (and its menu's Quit) is hidden.
    /// </summary>
    public void ShowQuit(bool visible) => QuitItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void ApplyTheme(AppTheme theme) => RootGrid.RequestedTheme = theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag as string == "Quit")
        {
            QuitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page))
        {
            // The Shortcuts page works with the registered shortcuts alone; every other page gets both
            ContentFrame.Navigate(page, page == typeof(ShortcutsPage) ? _shortcuts : _context);
        }
    }
}
