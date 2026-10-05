using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Scenes;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views;

/// <summary>
/// Everything inside the flyout window: the empty, picker and device pages plus the footer.
/// </summary>
public sealed partial class FlyoutView : UserControl
{
    private bool _deviceVisible;

    public FlyoutView(FlyoutViewModel viewModel)
    {
        ViewModel = viewModel;
        _deviceVisible = viewModel.IsDeviceVisible;
        InitializeComponent();

        // Pages, footer and every row of the device page (one level into its sections, including
        // the ones wrapped for their disabled state) fade in and out and glide when the layout
        // around them changes
        foreach (var element in ContentRoot.Children.Concat(DevicePanel.Children))
        {
            ImplicitMotion.Attach(element);
            var section = element switch
            {
                ContentControl { Content: Panel wrapped } => wrapped,
                Panel panel when element != DevicePanel => panel,
                _ => null,
            };
            if (section is null)
            {
                continue;
            }
            foreach (var row in section.Children)
            {
                ImplicitMotion.Attach(row);
            }
        }

        // Picker entrance animation only when the picker is where you land; with a default
        // headset set it just flashes by while that headset connects
        UpdateHeadsetListTransitions();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FlyoutViewModel.HasDefaultHeadset))
            {
                UpdateHeadsetListTransitions();
            }

            // Switching pages hides the focused button (Back, a picker row), and WinUI hands
            // keyboard focus to the next button, which pops its tooltip. Take focus quietly
            // instead; programmatic focus shows neither a tooltip nor a focus rectangle.
            if (e.PropertyName == nameof(FlyoutViewModel.IsDeviceVisible) && ViewModel.IsDeviceVisible != _deviceVisible)
            {
                _deviceVisible = ViewModel.IsDeviceVisible;
                Focus(FocusState.Programmatic);
            }
        };
    }

    // Only the entrance slide, and only without a default headset. No add/remove slides: a
    // headset disconnecting should just show what's left, not animate the list.
    private void UpdateHeadsetListTransitions()
    {
        var transitions = new TransitionCollection();
        if (!ViewModel.HasDefaultHeadset)
        {
            transitions.Add(new EntranceThemeTransition { IsStaggeringEnabled = false });
        }
        HeadsetList.ItemContainerTransitions = transitions;
    }

    public event EventHandler? CloseRequested;

    public FlyoutViewModel ViewModel { get; }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHeadsetClick(object sender, ItemClickEventArgs e) =>
        ViewModel.PickCommand.Execute(e.ClickedItem as HeadsetViewModel);

    // Hover and press backgrounds for a picker row; the row's BrushTransition fades between them
    private void OnPickerRowHover(object sender, PointerRoutedEventArgs e) => SetRowBackground(sender, "RowHoverBrush");

    private void OnPickerRowPressed(object sender, PointerRoutedEventArgs e) => SetRowBackground(sender, "RowPressedBrush");

    private void OnPickerRowRest(object sender, PointerRoutedEventArgs e) => SetRowBackground(sender, "RowRestBrush");

    private static void SetRowBackground(object sender, string brushKey)
    {
        if (sender is Grid row)
        {
            row.Background = (Brush)row.Resources[brushKey];
        }
    }

    private void OnPlaybackClick(object sender, RoutedEventArgs e)
    {
        // Device rides on Tag, like the scene buttons
        if (sender is FrameworkElement { Tag: PlaybackDevice device })
        {
            ViewModel.CurrentHeadset?.SwitchPlaybackCommand.Execute(device);
        }
    }

    private void OnSceneClick(object sender, RoutedEventArgs e)
    {
        // Scene rides on Tag: ItemsRepeater doesn't reliably set the button's DataContext
        if (sender is FrameworkElement { Tag: Scene scene })
        {
            ViewModel.CurrentHeadset?.ApplySceneCommand.Execute(scene);
        }
    }
}
