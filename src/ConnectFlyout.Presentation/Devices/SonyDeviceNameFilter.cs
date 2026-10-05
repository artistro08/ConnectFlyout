namespace ConnectFlyout.Presentation.Devices;

/// <summary>
/// Decides whether a Bluetooth device name belongs to a Sony headset.
/// </summary>
/// <remarks>
/// Same name markers upstream's platform discovery uses.
/// </remarks>
public static class SonyDeviceNameFilter
{
    private static readonly string[] Markers = ["WH-", "WF-", "WI-", "MDR-", "LINKBUDS", "ULT WEAR", "SONY"];

    public static bool IsSony(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Markers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
