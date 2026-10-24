using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.Presentation.Scenes;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Picks a scene icon from <see cref="SceneIcon.All"/>, narrowed by a search box. The current
/// one starts selected and stays the answer until another is picked.
/// </summary>
public sealed partial class IconPickerDialog : ContentDialog
{
    private string _selected;

    public IconPickerDialog(string currentGlyph)
    {
        InitializeComponent();
        _selected = currentGlyph;
        Filter("");
    }

    /// <summary>
    /// The chosen glyph; the current one until another is picked.
    /// </summary>
    public string SelectedGlyph => _selected;

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Filter(SearchBox.Text);

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A selection that drops out of the search results keeps the last choice
        if (IconGrid.SelectedItem is SceneIcon icon)
        {
            _selected = icon.Glyph;
        }
    }

    private void Filter(string query)
    {
        var icons = SceneIcon.Search(query);
        IconGrid.ItemsSource = icons;
        IconGrid.SelectedItem = icons.FirstOrDefault(icon => icon.Glyph == _selected);
        NoMatch.Visibility = icons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
