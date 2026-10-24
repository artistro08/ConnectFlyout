using Microsoft.UI.Xaml;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// The flyout's scenes: a card each, plus Add scene, which opens <see cref="SceneEditorPage"/>.
/// </summary>
public sealed partial class NoiseScenesPage : SettingsPageBase
{
    public NoiseScenesPage()
    {
        InitializeComponent();
    }

    private void OnAddSceneClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginNewScene();
        Frame.Navigate(typeof(SceneEditorPage), ViewModel);
    }

    // The scene rides on Tag: ItemsRepeater doesn't reliably set the button's DataContext
    private void OnActivateSceneClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SceneEditorViewModel scene })
        {
            ViewModel.ActivateScene(scene);
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
