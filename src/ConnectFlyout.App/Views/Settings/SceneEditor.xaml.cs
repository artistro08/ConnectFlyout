using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// A scene's settings, one per row: inside a scene's card and on the New scene page.
/// </summary>
public sealed partial class SceneEditor : UserControl
{
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene),
        typeof(SceneEditorViewModel),
        typeof(SceneEditor),
        new PropertyMetadata(null, (sender, _) => ((SceneEditor)sender).Bindings.Update()));

    public SceneEditor()
    {
        InitializeComponent();
    }

    public SceneEditorViewModel? Scene
    {
        get => (SceneEditorViewModel?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    private async void OnChangeIconClick(object sender, RoutedEventArgs e)
    {
        if (Scene is null)
        {
            return;
        }
        var dialog = new IconPickerDialog(Scene.Glyph) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.SelectedGlyph is { } glyph)
        {
            Scene.Glyph = glyph;
        }
    }
}
