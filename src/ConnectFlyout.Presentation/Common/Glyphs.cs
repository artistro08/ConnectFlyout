namespace ConnectFlyout.Presentation.Common;

/// <summary>
/// Segoe Fluent Icons glyphs the view models pick at runtime.
/// </summary>
public static class Glyphs
{
    public const string BatteryUnknown = "";

    /// <summary>
    /// Battery0 (E850) to Battery9 (E859) in 10% steps, Battery10 (E83F) at 100%.
    /// </summary>
    public static string Battery(int? level)
    {
        if (level is null)
        {
            return BatteryUnknown;
        }
        var step = Math.Clamp(level.Value / 10, 0, 10);
        return step == 10 ? "" : ((char)(0xE850 + step)).ToString();
    }
}
