namespace ConnectFlyout.Presentation.Devices;

/// <summary>
/// A paired Bluetooth device as Windows reports it.
/// </summary>
/// <param name="Id">Windows device ID.</param>
/// <param name="Name">Friendly name, e.g. "WF-1000XM6".</param>
/// <param name="Address">Bluetooth address, e.g. "ac:80:0a:12:34:56".</param>
/// <param name="IsConnected">True while Windows has the device connected.</param>
public sealed record BluetoothDeviceInfo(string Id, string Name, string Address, bool IsConnected);

/// <summary>
/// Reports paired Bluetooth devices as they're found, connect, disconnect or get unpaired.
/// </summary>
public interface IBluetoothDeviceSource
{
    /// <summary>
    /// Raised when a device is found or any of its reported values change.
    /// </summary>
    event EventHandler<BluetoothDeviceInfo>? DeviceChanged;

    /// <summary>
    /// Raised with the Windows device ID when a device is unpaired.
    /// </summary>
    event EventHandler<string>? DeviceRemoved;

    void StartWatching();

    void StopWatching();
}
