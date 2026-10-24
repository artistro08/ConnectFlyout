using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ConnectFlyout.App.Interop;
using ConnectFlyout.App.Views;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.Shortcuts;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// What Settings › Shortcuts needs: the registered shortcuts, the settings they're saved in, and the name of the
/// other device connected to the headphones (null when unknown), for the "Play on …" shortcut's name.
/// </summary>
public sealed record ShortcutsContext(GlobalShortcuts Shortcuts, AppSettings Settings, Func<string?> OtherDeviceName);

/// <summary>
/// Settings › Shortcuts: one PowerToys shortcut picker per noise mode and per scene, as in Leaf Calendar (press the
/// new keys in its dialog; Reset picks the default, Clear turns it off; scenes have no default, so Reset clears). A
/// combination another app holds shows a warning under its row and asks for another. Each row is a
/// <see cref="ShortcutRow"/>.
/// </summary>
public sealed partial class ShortcutsPage : Page
{
    private readonly List<ShortcutRow> _rows = [];
    private ShortcutsContext _context = null!;

    public ShortcutsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        _context = (ShortcutsContext)e.Parameter;
        BuildRows();
        Unloaded += OnUnloaded;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => DetachRows();

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachRows();

    // The noise mode and flyout rows, then one per scene. Scenes change on their own page, so the
    // rows are built on each visit.
    private void BuildRows()
    {
        DetachRows();
        Rows.Children.Clear();
        SceneRows.Children.Clear();

        foreach (var action in ShortcutActions.All)
        {
            AddRow(Rows, ShortcutTarget.For(action), IconFor(action));
        }
        foreach (var scene in _context.Settings.Scenes)
        {
            if (scene.Id is { } id)
            {
                AddRow(SceneRows, ShortcutTarget.ForScene(id), new FontIcon { Glyph = scene.Glyph, Style = IconStyle });
            }
        }
        NoScenes.Visibility = SceneRows.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Style IconStyle => (Style)Application.Current.Resources["SettingsCardIconStyle"];

    // The flyout's own noise mode icons for the mode rows; a new window, a laptop and a phone for the rest
    private static FrameworkElement IconFor(ShortcutAction action) => action switch
    {
        ShortcutAction.OpenFlyout     => new FontIcon { Glyph = "\uE8A7", Style = IconStyle },
        ShortcutAction.PlaybackThisPc => new FontIcon { Glyph = "\uE7F8", Style = IconStyle },
        ShortcutAction.PlaybackOther  => new FontIcon { Glyph = "\uE717", Style = IconStyle },
        _ => new NoiseModeIcon { Mode = ShortcutActions.NoiseModeFor(action), Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
    };

    // A card with the icon, the name and the picker, and the warning under it
    private void AddRow(Panel panel, ShortcutTarget target, FrameworkElement icon)
    {
        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(icon);
        var title = new TextBlock { Text = target.Name(_context), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        var control = new ShortcutControl();
        Grid.SetColumn(control, 2);
        grid.Children.Add(title);
        grid.Children.Add(control);

        var warning = new InfoBar { Severity = InfoBarSeverity.Warning, IsClosable = false, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(warning, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);

        panel.Children.Add(new Border { Style = (Style)Application.Current.Resources["SettingsCardStyle"], Child = grid });
        panel.Children.Add(warning);
        _rows.Add(new ShortcutRow(_context, target, control, warning, title));
    }

    private void DetachRows()
    {
        foreach (var row in _rows)
        {
            row.Detach();
        }
        _rows.Clear();
    }
}
