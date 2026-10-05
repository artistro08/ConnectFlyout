using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.App.Views;

/// <summary>
/// One noise control choice (Off, ANC, Ambient) in the flyout, shaped like a Quick Settings tile.
/// </summary>
public sealed partial class NoiseModeTile : UserControl
{
    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(NoiseMode), typeof(NoiseModeTile), new PropertyMetadata(NoiseMode.Off));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(NoiseModeTile), new PropertyMetadata(""));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(NoiseModeTile), new PropertyMetadata(false));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(NoiseModeTile), new PropertyMetadata(null));

    public NoiseModeTile()
    {
        InitializeComponent();
    }

    public NoiseMode Mode
    {
        get => (NoiseMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Runs with the mode's name (the headset view model's SetNoiseModeCommand).
    /// </summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    // A click toggles the button on its own; the tile shows what the headset view model says
    // (clicking the selected mode keeps it selected). Click runs before the command.
    private void OnClick(object sender, RoutedEventArgs e) => TileButton.IsChecked = IsSelected;
}
