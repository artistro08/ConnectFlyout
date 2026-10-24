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

    public static readonly DependencyProperty RowPaddingProperty = DependencyProperty.Register(
        nameof(RowPadding),
        typeof(Thickness),
        typeof(SceneEditor),
        new PropertyMetadata(new Thickness(58, 14, 44, 14)));

    // Only one ContentDialog can be open at a time; a second Change click waits its turn
    private bool _picking;

    public SceneEditor()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Each row's padding: the expander row's by default (its right side clears the chevron).
    /// </summary>
    public Thickness RowPadding
    {
        get => (Thickness)GetValue(RowPaddingProperty);
        set => SetValue(RowPaddingProperty, value);
    }

    public SceneEditorViewModel? Scene
    {
        get => (SceneEditorViewModel?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    private async void OnChangeIconClick(object sender, RoutedEventArgs e)
    {
        if (Scene is null || _picking)
        {
            return;
        }
        _picking = true;
        try
        {
            var dialog = new IconPickerDialog(Scene.Glyph) { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                Scene.Glyph = dialog.SelectedGlyph;
            }
        }
        catch (Exception)
        {
            // Another dialog (the shortcut picker, say) is up; WinUI refuses a second one
        }
        finally
        {
            _picking = false;
        }
    }
}
