using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.Presentation.Shortcuts;

/// <summary>
/// What a global shortcut does: switch the headphones to one noise mode, or open the flyout. The values are the
/// hotkey IDs registered with Windows.
/// </summary>
public enum ShortcutAction
{
    NoiseCancelling = 1,
    Ambient = 2,
    Off = 3,
    OpenFlyout = 4,
}

/// <summary>
/// The noise mode (for the noise-mode ones), name and default combination of each <see cref="ShortcutAction"/>.
/// </summary>
public static class ShortcutActions
{
    public static IReadOnlyList<ShortcutAction> All { get; } =
    [
        ShortcutAction.NoiseCancelling,
        ShortcutAction.Ambient,
        ShortcutAction.Off,
        ShortcutAction.OpenFlyout,
    ];

    public static NoiseMode NoiseModeFor(ShortcutAction action) => action switch
    {
        ShortcutAction.Ambient => NoiseMode.Ambient,
        ShortcutAction.Off     => NoiseMode.Off,
        _                      => NoiseMode.NoiseCancelling,
    };

    /// <summary>
    /// The setting's name, as Settings › Shortcuts and the picker's dialog title show it.
    /// </summary>
    public static string NameFor(ShortcutAction action) => action switch
    {
        ShortcutAction.Ambient    => "Ambient sound",
        ShortcutAction.Off        => "Noise control off",
        ShortcutAction.OpenFlyout => "Open the flyout",
        _                         => "Noise canceling",
    };

    public static string DefaultFor(ShortcutAction action) => action switch
    {
        ShortcutAction.Ambient    => "Win+F11",
        ShortcutAction.Off        => "Win+F6",
        ShortcutAction.OpenFlyout => "Win+F9",
        _                         => "Win+F10",
    };
}
