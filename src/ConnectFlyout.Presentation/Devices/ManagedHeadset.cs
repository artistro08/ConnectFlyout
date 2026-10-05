using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.Presentation.Devices;

/// <summary>
/// A paired Sony headset, whether Windows has it connected, and the state of the app's control
/// link to it.
/// </summary>
public sealed class ManagedHeadset
{
    internal ManagedHeadset(string deviceId, string address, string name, IHeadset headset)
    {
        DeviceId = deviceId;
        Id = address;
        Name = name;
        Headset = headset;
    }

    /// <summary>
    /// Windows device ID.
    /// </summary>
    public string DeviceId { get; }

    /// <summary>
    /// Upper-case Bluetooth address. Stable across restarts, so settings are keyed on it.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Name in Windows. Follows renames.
    /// </summary>
    public string Name { get; internal set; }

    public IHeadset Headset { get; }

    /// <summary>
    /// Whether Windows has the headset connected (audio). The control link can only open while
    /// it does, apart from a one-off <see cref="HeadsetManager.Reconnect"/>.
    /// </summary>
    public bool IsWindowsConnected { get; internal set; }

    public HeadsetConnectionState ConnectionState { get; internal set; }

    internal CancellationTokenSource? ConnectLoop { get; set; }

    internal Task? ConnectTask { get; set; }

    /// <summary>
    /// The link dropped while a connect was in flight, so a connect that still reports success
    /// is treated as failed.
    /// </summary>
    internal bool DroppedWhileConnecting { get; set; }
}
