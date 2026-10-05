using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.Navigation;

/// <summary>
/// Which page the flyout shows.
/// </summary>
public enum FlyoutPageKind
{
    Empty,
    Picker,
    Device,
}

/// <summary>
/// The page to show, the headset it's for, and whether the back arrow is visible.
/// </summary>
public sealed record FlyoutRoute(FlyoutPageKind Kind, string? HeadsetId, bool ShowBack);

/// <summary>
/// A listed headset and whether it's available (Windows has it connected and it isn't released).
/// </summary>
public sealed record HeadsetAvailability(string Id, bool IsAvailable);

/// <summary>
/// Picks the flyout page from the listed headsets and the remembered choice.
/// </summary>
/// <remarks>
/// Rules, first match wins:
/// 1. No headsets: empty page.
/// 2. Just backed out: the picker, until a headset is picked.
/// 3. A headset picked while unavailable: its page, so it can be reconnected.
/// 4. The remembered headset is available, or nothing else is: its page.
/// 5. Exactly one available headset (or only one listed): its page.
/// 6. Otherwise: the picker.
/// The device page always has a back arrow, so the picker is always reachable. The remembered
/// choice survives disconnects, so its page comes back when the headset does; only
/// <see cref="Back"/> clears it.
/// </remarks>
public sealed class FlyoutNavigator
{
    private readonly AppSettings _settings;

    // Not saved: they only last until the app closes
    private bool _showPicker;
    private string? _pickedWhileUnavailable;

    public FlyoutNavigator(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// True once a headset was picked and not backed out of.
    /// </summary>
    public bool HasRemembered => _settings.RememberedHeadsetId is not null;

    public FlyoutRoute Resolve(IReadOnlyList<HeadsetAvailability> headsets)
    {
        ArgumentNullException.ThrowIfNull(headsets);

        if (headsets.Count == 0)
        {
            return new FlyoutRoute(FlyoutPageKind.Empty, null, false);
        }
        if (_showPicker)
        {
            return new FlyoutRoute(FlyoutPageKind.Picker, null, false);
        }
        if (_pickedWhileUnavailable is { } picked && headsets.Any(headset => headset.Id == picked))
        {
            return Device(picked);
        }

        var available = headsets.Where(headset => headset.IsAvailable).ToList();
        var remembered = headsets.FirstOrDefault(headset => headset.Id == _settings.RememberedHeadsetId);
        if (remembered is not null && (remembered.IsAvailable || available.Count == 0))
        {
            return Device(remembered.Id);
        }
        if (available.Count == 1)
        {
            return Device(available[0].Id);
        }
        if (headsets.Count == 1)
        {
            return Device(headsets[0].Id);
        }
        return new FlyoutRoute(FlyoutPageKind.Picker, null, false);
    }

    /// <summary>
    /// Remembers the headset. If it's unavailable right now, its page stays up anyway (until the
    /// app closes), so it can be reconnected from there.
    /// </summary>
    public void Pick(string headsetId, bool isAvailable = true)
    {
        _settings.RememberedHeadsetId = headsetId;
        _showPicker = false;
        _pickedWhileUnavailable = isAvailable ? null : headsetId;
    }

    public void Back()
    {
        _settings.RememberedHeadsetId = null;
        _showPicker = true;
        _pickedWhileUnavailable = null;
    }

    private static FlyoutRoute Device(string headsetId) => new(FlyoutPageKind.Device, headsetId, true);
}
