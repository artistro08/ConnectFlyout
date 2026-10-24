using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// The flyout's scenes: a card each, Add scene (opens <see cref="SceneEditorPage"/>) and
/// Reorder (opens <see cref="ReorderScenesPage"/>).
/// </summary>
public sealed partial class ScenesPage : SettingsPageBase
{
    public ScenesPage()
    {
        InitializeComponent();
    }

    private void OnAddSceneClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginNewScene();
        Frame.Navigate(typeof(SceneEditorPage), ViewModel);
    }

    private void OnReorderClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ReorderScenesPage), ViewModel);

    // The scene rides on Tag: ItemsRepeater doesn't reliably set the control's DataContext
    private void OnSceneToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { Tag: SceneEditorViewModel scene } toggle)
        {
            ViewModel.ToggleScene(scene, toggle.IsOn);
        }
    }

    private void OnDeleteSceneClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SceneEditorViewModel scene })
        {
            ViewModel.DeleteScene(scene);
        }
    }
}
