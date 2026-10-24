using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.Presentation.Shortcuts;

/// <summary>
/// What a global shortcut does: switch the headphones to one noise mode, open the flyout, or move the audio to this
/// PC or to the other connected device. The values are the hotkey IDs registered with Windows.
/// </summary>
public enum ShortcutAction
{
    NoiseCancelling = 1,
    Ambient = 2,
    Off = 3,
    OpenFlyout = 4,
    PlaybackThisPc = 5,
    PlaybackOther = 6,
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
        ShortcutAction.PlaybackThisPc,
        ShortcutAction.PlaybackOther,
    ];

    /// <summary>
    /// The two shortcuts that move the audio instead of changing the noise mode.
    /// </summary>
    public static bool IsPlayback(ShortcutAction action) => action is ShortcutAction.PlaybackThisPc or ShortcutAction.PlaybackOther;

    /// <summary>
    /// The noise mode a noise mode shortcut sets; only meaningful for those three.
    /// </summary>
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
        ShortcutAction.Ambient        => "Ambient sound",
        ShortcutAction.Off            => "Noise control off",
        ShortcutAction.OpenFlyout     => "Open the flyout",
        ShortcutAction.PlaybackThisPc => "Play on this PC",
        ShortcutAction.PlaybackOther  => "Play on the other device",
        _                             => "Noise canceling",
    };

    /// <summary>
    /// The combination a fresh install starts with; "" for the playback shortcuts, which start off.
    /// </summary>
    public static string DefaultFor(ShortcutAction action) => action switch
    {
        ShortcutAction.Ambient        => "Win+F11",
        ShortcutAction.Off            => "Win+F6",
        ShortcutAction.OpenFlyout     => "Win+F9",
        ShortcutAction.PlaybackThisPc => "",
        ShortcutAction.PlaybackOther  => "",
        _                             => "Win+F10",
    };
}
