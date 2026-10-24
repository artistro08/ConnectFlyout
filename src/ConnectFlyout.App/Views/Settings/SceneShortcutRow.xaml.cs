using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// A scene card's Shortcut row: the same picker as Settings › Shortcuts, wired through
/// <see cref="ShortcutRow"/>. It finds the shortcuts through the page it sits on, since a data
/// template can't be handed them.
/// </summary>
public sealed partial class SceneShortcutRow : UserControl
{
    public static readonly DependencyProperty SceneIdProperty =
        DependencyProperty.Register(nameof(SceneId), typeof(string), typeof(SceneShortcutRow), new PropertyMetadata(null, (sender, _) => ((SceneShortcutRow)sender).Rebind()));

    private ShortcutRow? _row;

    public SceneShortcutRow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string? SceneId
    {
        get => (string?)GetValue(SceneIdProperty);
        set => SetValue(SceneIdProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Rebind();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _row?.Detach();
        _row = null;
    }

    // Wires the picker to the scene at hand. A card the repeater reuses for another scene
    // comes through here again with the new ID, so the old wiring goes first.
    private void Rebind()
    {
        _row?.Detach();
        _row = null;
        if (!IsLoaded || SceneId is not { } id || PageAbove() is not { } page)
        {
            return;
        }
        _row = new ShortcutRow(page.Shortcuts, ShortcutTarget.ForScene(id), Picker, Warning);
    }

    private SettingsPageBase? PageAbove()
    {
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is SettingsPageBase page)
            {
                return page;
            }
        }
        return null;
    }
}
