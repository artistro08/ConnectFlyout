using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Notifications;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.Tests.Fakes;

internal sealed class FakeDeviceSource : IBluetoothDeviceSource
{
    public event EventHandler<BluetoothDeviceInfo>? DeviceChanged;

    public event EventHandler<string>? DeviceRemoved;

    public bool IsWatching { get; private set; }

    public void StartWatching() => IsWatching = true;

    public void StopWatching() => IsWatching = false;

    public void Report(BluetoothDeviceInfo device) => DeviceChanged?.Invoke(this, device);

    public void Remove(string deviceId) => DeviceRemoved?.Invoke(this, deviceId);
}

internal sealed class FakeNotificationService : INotificationService
{
    public List<(string DeviceName, int Level)> Shown { get; } = [];

    public List<string> HeadsetIds { get; } = [];

    public void ShowLowBattery(string headsetId, string deviceName, int level)
    {
        HeadsetIds.Add(headsetId);
        Shown.Add((deviceName, level));
    }
}

internal sealed class FakeStartupTaskService : IStartupTaskService
{
    public bool Enabled { get; set; }

    /// <summary>
    /// When true, enabling is refused like a policy-disabled startup task.
    /// </summary>
    public bool RefuseEnable { get; set; }

    public Task<bool> IsEnabledAsync() => Task.FromResult(Enabled);

    public Task<bool> SetEnabledAsync(bool enabled)
    {
        Enabled = enabled && !RefuseEnable;
        return Task.FromResult(Enabled);
    }
}

/// <summary>
/// Records Bluetooth connect/disconnect requests; <see cref="Accept"/> decides what Windows answers.
/// </summary>
internal sealed class FakeBluetoothAudio : IBluetoothAudio
{
    public List<string> Requests { get; } = [];

    public List<string> DisconnectRequests { get; } = [];

    public bool Accept { get; set; } = true;

    public Task<bool> ConnectAsync(string bluetoothAddress)
    {
        Requests.Add(bluetoothAddress);
        return Task.FromResult(Accept);
    }

    public Task<bool> DisconnectAsync(string bluetoothAddress)
    {
        DisconnectRequests.Add(bluetoothAddress);
        return Task.FromResult(Accept);
    }
}
