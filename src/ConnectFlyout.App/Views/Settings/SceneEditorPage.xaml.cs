using Microsoft.UI.Xaml;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// The New scene page: the draft's rows with Save and Cancel, both returning to Noise &amp; scenes.
/// </summary>
public sealed partial class SceneEditorPage : SettingsPageBase
{
    public SceneEditorPage()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelNewScene();
        Frame.GoBack();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CommitNewScene();
        Frame.GoBack();
    }
}
