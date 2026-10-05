using Microsoft.UI.Xaml;
using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.App;

/// <summary>
/// x:Bind functions for the scene buttons (accent style when active), the noise control tiles'
/// command parameter and the headphone list's status line (caution color for an error).
/// </summary>
public static class TileStyles
{
    public static Style PickScene(bool active) =>
        (Style)Application.Current.Resources[active ? "SceneButtonActiveStyle" : "SceneButtonStyle"];

    /// <summary>
    /// A noise mode as the text SetNoiseModeCommand takes.
    /// </summary>
    public static string ModeName(NoiseMode mode) => mode.ToString();

    public static Style PickStatus(bool error) =>
        (Style)Application.Current.Resources[error ? "CautionTextStyle" : "SecondaryTextStyle"];
}
