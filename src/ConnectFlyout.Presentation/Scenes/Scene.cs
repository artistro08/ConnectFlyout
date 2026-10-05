using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.Presentation.Scenes;

/// <summary>
/// A saved noise control combination shown as a button in the flyout.
/// </summary>
/// <param name="Name">Button label.</param>
/// <param name="Glyph">Segoe Fluent Icons glyph.</param>
/// <param name="Setting">What the button applies.</param>
public sealed record Scene(string Name, string Glyph, NoiseControlSetting Setting)
{
    /// <summary>
    /// Office scene icon: a briefcase.
    /// </summary>
    public const string OfficeGlyph = "";

    /// <summary>
    /// Office icon before 1.0.0.0's update (people). Saved scenes that still have it get
    /// <see cref="OfficeGlyph"/> instead.
    /// </summary>
    public const string OldOfficeGlyph = "";

    /// <summary>
    /// Scenes a fresh install starts with: Focus (moon), Office (briefcase), Aware (walking).
    /// </summary>
    public static IReadOnlyList<Scene> Defaults { get; } =
    [
        new Scene("Focus", "", new NoiseControlSetting(NoiseMode.NoiseCancelling, 0, false)),
        new Scene("Office", OfficeGlyph, new NoiseControlSetting(NoiseMode.Ambient, 10, true)),
        new Scene("Aware", "", new NoiseControlSetting(NoiseMode.Ambient, 20, false)),
    ];
}
