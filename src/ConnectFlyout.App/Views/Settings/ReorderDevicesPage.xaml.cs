using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Drag-to-reorder list of the headphones; each drag saves the new order. Done goes back.
/// </summary>
public sealed partial class ReorderDevicesPage : SettingsPageBase
{
    public ReorderDevicesPage()
    {
        InitializeComponent();
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => ViewModel.SaveHeadsetOrder();

    private void OnDoneClick(object sender, RoutedEventArgs e) => Frame.GoBack();
}
