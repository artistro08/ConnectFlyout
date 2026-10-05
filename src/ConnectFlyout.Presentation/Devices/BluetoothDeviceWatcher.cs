using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace ConnectFlyout.Presentation.Devices;

/// <summary>
/// <see cref="IBluetoothDeviceSource"/> over a Windows <see cref="DeviceWatcher"/> for paired
/// Bluetooth Classic devices.
/// </summary>
public sealed class BluetoothDeviceWatcher : IBluetoothDeviceSource, IDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, BluetoothDeviceInfo> _devices = [];
    private DeviceWatcher? _watcher;

    public event EventHandler<BluetoothDeviceInfo>? DeviceChanged;

    public event EventHandler<string>? DeviceRemoved;

    public void StartWatching()
    {
        if (_watcher is not null)
        {
            return;
        }

        _watcher = DeviceInformation.CreateWatcher(
            BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            [IsConnectedProperty, AddressProperty],
            DeviceInformationKind.AssociationEndpoint);
        _watcher.Added += OnAdded;
        _watcher.Updated += OnUpdated;
        _watcher.Removed += OnRemoved;
        _watcher.Start();
    }

    public void StopWatching()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.Added -= OnAdded;
        _watcher.Updated -= OnUpdated;
        _watcher.Removed -= OnRemoved;
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            _watcher.Stop();
        }
        _watcher = null;
    }

    public void Dispose() => StopWatching();

    private static bool ReadBool(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is bool flag && flag;

    private static string ReadString(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is string text ? text : "";

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        var device = new BluetoothDeviceInfo(
            info.Id,
            info.Name,
            ReadString(info.Properties, AddressProperty),
            ReadBool(info.Properties, IsConnectedProperty));

        lock (_gate)
        {
            _devices[info.Id] = device;
        }
        DeviceChanged?.Invoke(this, device);
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        BluetoothDeviceInfo? device;
        lock (_gate)
        {
            if (!_devices.TryGetValue(update.Id, out device))
            {
                return;
            }
            if (update.Properties.ContainsKey(IsConnectedProperty))
            {
                device = device with { IsConnected = ReadBool(update.Properties, IsConnectedProperty) };
                _devices[update.Id] = device;
            }
        }
        DeviceChanged?.Invoke(this, device);
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_gate)
        {
            if (!_devices.Remove(update.Id))
            {
                return;
            }
        }
        DeviceRemoved?.Invoke(this, update.Id);
    }
}
