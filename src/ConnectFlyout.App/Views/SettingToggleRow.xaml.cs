using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectFlyout.App.Views;

/// <summary>
/// A setting's name and subtitle with an on/off switch, like the web app's setting rows.
/// </summary>
public sealed partial class SettingToggleRow : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingToggleRow), new PropertyMetadata(""));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(SettingToggleRow), new PropertyMetadata(""));

    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(SettingToggleRow), new PropertyMetadata(false));

    public SettingToggleRow()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }
}
