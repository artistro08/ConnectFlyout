#pragma once

#include "HeadsetClient.g.h"

#include "sony/protocol/HeadsetController.h"

#include <functional>
#include <memory>
#include <mutex>

namespace winrt::ConnectFlyout::Core::implementation {

struct HeadsetClient : HeadsetClientT<HeadsetClient> {
    explicit HeadsetClient(hstring const& deviceName);

    static void SetLogHandler(Core::NativeLogHandler const& handler);
    static void SetDebugLogging(bool enabled);
    static com_array<Core::EqualizerPresetInfo> GetEqualizerPresets();
    static Windows::Foundation::IAsyncOperation<bool> ConnectAudioAsync(hstring bluetoothAddress);
    static Windows::Foundation::IAsyncOperation<bool> DisconnectAudioAsync(hstring bluetoothAddress);

    hstring DeviceName() const;
    hstring ModelName() const;
    bool IsKnownModel() const;
    Core::ProtocolGeneration Protocol() const;
    Core::HeadsetCapabilities Capabilities() const;
    bool IsConnected() const;
    Core::HeadsetState State() const;

    Windows::Foundation::IAsyncAction ConnectAsync(hstring bluetoothAddress);
    void Disconnect();
    Windows::Foundation::IAsyncAction RefreshBatteryAsync();
    Windows::Foundation::IAsyncAction SetNoiseControlAsync(Core::NoiseControlInfo value);
    Windows::Foundation::IAsyncAction SetEqualizerPresetAsync(int32_t preset);
    Windows::Foundation::IAsyncAction SetEqualizerCustomAsync(Core::EqualizerInfo value);
    Windows::Foundation::IAsyncAction SetConnectionQualityAsync(bool prioritizeStableConnection);
    Windows::Foundation::IAsyncAction SetDseeAsync(bool enabled);
    Windows::Foundation::IAsyncAction SetVptAsync(int32_t preset);
    Windows::Foundation::IAsyncAction SetSoundPositionAsync(int32_t position);
    Windows::Foundation::IAsyncAction SetVoiceGuidanceAsync(int32_t value);
    Windows::Foundation::IAsyncAction PowerOffAsync();
    Windows::Foundation::IAsyncAction SetSpeakToChatAsync(bool enabled);
    Windows::Foundation::IAsyncAction SetAdaptiveVolumeAsync(bool enabled);
    Windows::Foundation::IAsyncAction SetAutoPowerOffAsync(int32_t index);
    Windows::Foundation::IAsyncAction SwitchPlaybackAsync(hstring address);
    com_array<Core::PlaybackDeviceInfo> GetPlaybackDevices() const;

    event_token StateChanged(Windows::Foundation::TypedEventHandler<Core::HeadsetClient, Core::HeadsetState> const& handler);
    void StateChanged(event_token const& token) noexcept;
    event_token Disconnected(Windows::Foundation::TypedEventHandler<Core::HeadsetClient, Windows::Foundation::IInspectable> const& handler);
    void Disconnected(event_token const& token) noexcept;

    void Close();

private:
    using Command = std::function<void(sony::protocol::HeadsetController&)>;

    Windows::Foundation::IAsyncAction RunAsync(Command command);

    // Registers the controller callbacks, once, on the first event subscription.
    void HookControllerCallbacks();

    hstring m_deviceName;
    // Shared so a command still running on the thread pool keeps it alive.
    std::shared_ptr<sony::protocol::HeadsetController> m_controller;

    event<Windows::Foundation::TypedEventHandler<Core::HeadsetClient, Core::HeadsetState>> m_stateChanged;
    event<Windows::Foundation::TypedEventHandler<Core::HeadsetClient, Windows::Foundation::IInspectable>> m_disconnected;
    std::once_flag m_callbacksHooked;
};

} // namespace winrt::ConnectFlyout::Core::implementation

namespace winrt::ConnectFlyout::Core::factory_implementation {

struct HeadsetClient : HeadsetClientT<HeadsetClient, implementation::HeadsetClient> {};

} // namespace winrt::ConnectFlyout::Core::factory_implementation
