using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Drag-to-reorder list of the scenes; each drag saves the new order. Done goes back.
/// </summary>
public sealed partial class ReorderScenesPage : SettingsPageBase
{
    public ReorderScenesPage()
    {
        InitializeComponent();
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => ViewModel.SaveScenes();

    private void OnDoneClick(object sender, RoutedEventArgs e) => Frame.GoBack();
}
