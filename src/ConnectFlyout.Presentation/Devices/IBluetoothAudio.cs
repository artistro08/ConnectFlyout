using Core = ConnectFlyout.Core;

namespace ConnectFlyout.Presentation.Devices;

/// <summary>
/// Connects a paired headset's audio from Windows' side, like Connect in Settings > Sound.
/// </summary>
public interface IBluetoothAudio
{
    /// <summary>
    /// Asks Windows to connect the headset with this Bluetooth address.
    /// </summary>
    /// <returns>
    /// True when Windows took the request. The headset still has to be on and in range; the
    /// connection shows up as a Windows connect like any other.
    /// </returns>
    Task<bool> ConnectAsync(string bluetoothAddress);

    /// <summary>
    /// Asks Windows to disconnect the headset's full Bluetooth link.
    /// </summary>
    Task<bool> DisconnectAsync(string bluetoothAddress);
}

/// <summary>
/// <see cref="IBluetoothAudio"/> through the native component.
/// </summary>
public sealed class WindowsBluetoothAudio : IBluetoothAudio
{
    public Task<bool> ConnectAsync(string bluetoothAddress) => Core.HeadsetClient.ConnectAudioAsync(bluetoothAddress).AsTask();

    public Task<bool> DisconnectAsync(string bluetoothAddress) =>
        Core.HeadsetClient.DisconnectAudioAsync(bluetoothAddress).AsTask();
}
