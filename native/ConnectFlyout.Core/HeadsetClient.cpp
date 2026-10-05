#include "pch.h"

#include "HeadsetClient.h"
#if __has_include("HeadsetClient.g.cpp")
#include "HeadsetClient.g.cpp"
#endif

#include "BluetoothAudio.h"
#include "sony/protocol/DeviceProfileRegistry.h"
#include "sony/protocol/EqualizerPresets.h"
#include "sony/protocol/ErrorMapping.h"
#include "sony/transport/BluetoothAddress.h"
#include "sony/transport/Logger.h"
#include "sony/transport/WindowsRfcommTransport.h"

#include <mutex>
#include <string>

using namespace winrt::Windows::Foundation;

namespace winrt::ConnectFlyout::Core::implementation {

namespace {

// =========================================================================
// LOGGING
// =========================================================================

std::mutex& logHandlerMutex() {
    static std::mutex mutex;
    return mutex;
}

struct LogHandlerHolder {
    Core::NativeLogHandler handler{nullptr};
};

// Never destroyed: releasing a managed delegate while the process shuts down
// can crash, so the last handler is deliberately leaked.
Core::NativeLogHandler& logHandler() {
    static auto* holder = new LogHandlerHolder{};
    return holder->handler;
}

Core::NativeLogLevel toNativeLevel(sony::LogLevel level) noexcept {
    switch (level) {
        case sony::LogLevel::Trace:
        case sony::LogLevel::Debug:
            return Core::NativeLogLevel::Debug;
        case sony::LogLevel::Info:
            return Core::NativeLogLevel::Information;
        case sony::LogLevel::Warn:
            return Core::NativeLogLevel::Warning;
        case sony::LogLevel::Error:
        case sony::LogLevel::Off:
            return Core::NativeLogLevel::Error;
    }
    return Core::NativeLogLevel::Information;
}

// =========================================================================
// CONVERSIONS
// =========================================================================

int32_t levelOrUnknown(const std::optional<int>& value) noexcept {
    return value.has_value() ? static_cast<int32_t>(*value) : -1;
}

Core::NoiseMode toNoiseMode(sony::protocol::NoiseControlMode mode) noexcept {
    switch (mode) {
        case sony::protocol::NoiseControlMode::NoiseCancelling:
            return Core::NoiseMode::NoiseCancelling;
        case sony::protocol::NoiseControlMode::Ambient:
            return Core::NoiseMode::Ambient;
        case sony::protocol::NoiseControlMode::Off:
            return Core::NoiseMode::Off;
    }
    return Core::NoiseMode::Off;
}

sony::protocol::NoiseControlMode fromNoiseMode(Core::NoiseMode mode) noexcept {
    switch (mode) {
        case Core::NoiseMode::NoiseCancelling:
            return sony::protocol::NoiseControlMode::NoiseCancelling;
        case Core::NoiseMode::Ambient:
            return sony::protocol::NoiseControlMode::Ambient;
        case Core::NoiseMode::Off:
            return sony::protocol::NoiseControlMode::Off;
    }
    return sony::protocol::NoiseControlMode::Off;
}

Core::HeadsetState toHeadsetState(const sony::protocol::DeviceState& state) {
    Core::HeadsetState result{};
    result.Battery.Main = levelOrUnknown(state.battery.main);
    result.Battery.Left = levelOrUnknown(state.battery.left);
    result.Battery.Right = levelOrUnknown(state.battery.right);
    result.Battery.CaseBattery = levelOrUnknown(state.battery.caseBattery);
    result.Battery.Charging = state.battery.charging;

    result.NoiseControl.Mode = toNoiseMode(state.noiseControl.mode);
    result.NoiseControl.AmbientLevel = state.noiseControl.ambientLevel;
    result.NoiseControl.FocusOnVoice = state.noiseControl.focusOnVoice;

    result.Equalizer.Preset = state.equalizer.preset;
    result.Equalizer.ClearBass = state.equalizer.clearBass;
    result.Equalizer.Band1 = state.equalizer.bands[0];
    result.Equalizer.Band2 = state.equalizer.bands[1];
    result.Equalizer.Band3 = state.equalizer.bands[2];
    result.Equalizer.Band4 = state.equalizer.bands[3];
    result.Equalizer.Band5 = state.equalizer.bands[4];

    result.Dsee = state.dsee;
    result.ConnectionQuality = state.connectionQuality;
    result.VoiceGuidance = state.voiceGuidance;
    result.Vpt = state.vpt;
    result.SoundPosition = state.soundPosition;
    result.SpeakToChat = state.speakToChat;
    result.AdaptiveVolume = state.adaptiveVolume;
    result.AutoPowerOff = state.autoPowerOff;
    result.Firmware = to_hstring(state.firmware);
    result.Codec = to_hstring(state.codec);
    return result;
}

} // namespace

// =========================================================================
// CONSTRUCTION
// =========================================================================

HeadsetClient::HeadsetClient(hstring const& deviceName)
    : m_deviceName(deviceName),
      m_controller(std::make_shared<sony::protocol::HeadsetController>(
          std::make_unique<sony::transport::WindowsRfcommTransport>(),
          to_string(deviceName))) {}

void HeadsetClient::HookControllerCallbacks() {
    // C++/WinRT has no post-construction hook (only final_release), and a weak reference
    // can't be taken in the constructor, so this runs on the first event subscription.
    std::call_once(m_callbacksHooked, [this] {
        weak_ref<HeadsetClient> weak = get_weak();

        m_controller->onStateChanged([weak](const sony::protocol::DeviceState& state) {
            if (auto self = weak.get()) {
                self->m_stateChanged(*self, toHeadsetState(state));
            }
        });
        m_controller->onDisconnected([weak] {
            if (auto self = weak.get()) {
                self->m_disconnected(*self, nullptr);
            }
        });
    });
}

// =========================================================================
// STATIC METHODS
// =========================================================================

void HeadsetClient::SetLogHandler(Core::NativeLogHandler const& handler) {
    {
        std::lock_guard lock(logHandlerMutex());
        logHandler() = handler;
    }

    sony::Logger::setLogSink([](sony::LogLevel level, std::string_view category, std::string_view message) {
        Core::NativeLogHandler current{nullptr};
        {
            std::lock_guard lock(logHandlerMutex());
            current = logHandler();
        }
        if (!current) {
            return;
        }
        try {
            current(toNativeLevel(level), to_hstring(std::string(category) + ": " + std::string(message)));
        } catch (...) {
            // Never let a logging failure take down the headset link.
        }
    });
}

void HeadsetClient::SetDebugLogging(bool enabled) {
    sony::Logger::setDeveloperMode(enabled);
    sony::Logger::setLogLevel(enabled ? sony::LogLevel::Debug : sony::LogLevel::Info);
}

IAsyncOperation<bool> HeadsetClient::ConnectAudioAsync(hstring bluetoothAddress) {
    const auto address = sony::transport::parseBluetoothAddress(to_string(bluetoothAddress));
    co_await resume_background();
    if (!address) {
        co_return false;
    }
    co_return sony::audio::requestAudioConnect(*address);
}

IAsyncOperation<bool> HeadsetClient::DisconnectAudioAsync(hstring bluetoothAddress) {
    const auto address = sony::transport::parseBluetoothAddress(to_string(bluetoothAddress));
    co_await resume_background();
    if (!address) {
        co_return false;
    }
    co_return sony::audio::requestAudioDisconnect(*address);
}
com_array<Core::EqualizerPresetInfo> HeadsetClient::GetEqualizerPresets() {
    const auto& presets = sony::protocol::equalizerPresets();

    com_array<Core::EqualizerPresetInfo> result(static_cast<uint32_t>(presets.size()));
    for (size_t i = 0; i < presets.size(); ++i) {
        result[static_cast<uint32_t>(i)] = Core::EqualizerPresetInfo{
            static_cast<int32_t>(presets[i].preset),
            to_hstring(presets[i].displayName),
        };
    }
    return result;
}

// =========================================================================
// PROPERTIES
// =========================================================================

hstring HeadsetClient::DeviceName() const {
    return m_deviceName;
}

hstring HeadsetClient::ModelName() const {
    const auto model = m_controller->profile().model;
    if (model == sony::protocol::SonyModel::Unknown) {
        return m_deviceName;
    }
    return to_hstring(sony::protocol::to_string(model));
}

bool HeadsetClient::IsKnownModel() const {
    return m_controller->profile().model != sony::protocol::SonyModel::Unknown;
}

Core::ProtocolGeneration HeadsetClient::Protocol() const {
    return m_controller->generation() == sony::protocol::ProtocolGeneration::V2
        ? Core::ProtocolGeneration::V2
        : Core::ProtocolGeneration::V1;
}

Core::HeadsetCapabilities HeadsetClient::Capabilities() const {
    const auto& capabilities = m_controller->profile().capabilities;

    Core::HeadsetCapabilities result{};
    result.DualBattery = capabilities.dualBattery;
    result.NoiseCancelling = capabilities.noiseCancelling;
    result.AmbientSound = capabilities.ambientSound;
    result.FocusOnVoice = capabilities.focusOnVoice;
    result.Equalizer = capabilities.equalizer;
    result.ClearBass = capabilities.clearBass;
    result.Dsee = capabilities.dsee;
    result.SpeakToChat = capabilities.speakToChat;
    result.AdaptiveVolume = capabilities.adaptiveVolume;
    result.AutoPowerOff = capabilities.autoPowerOff;
    result.FirmwareInfo = capabilities.firmwareInfo;
    result.CodecInfo = capabilities.codecInfo;
    result.ConnectionQuality = capabilities.connectionQuality;
    result.DseeExtreme = capabilities.dseeExtreme;
    result.VoiceGuidance = capabilities.voiceGuidance;
    result.Vpt = capabilities.vpt;
    result.SoundPosition = capabilities.soundPosition;
    result.PowerOff = capabilities.powerOff;
    result.AutoPowerOffWhenRemoved = capabilities.autoPowerOffWhenRemoved;
    return result;
}

bool HeadsetClient::IsConnected() const {
    return m_controller->isConnected();
}

Core::HeadsetState HeadsetClient::State() const {
    return toHeadsetState(m_controller->state());
}

com_array<Core::PlaybackDeviceInfo> HeadsetClient::GetPlaybackDevices() const {
    const auto devices = m_controller->state().playbackDevices;

    com_array<Core::PlaybackDeviceInfo> result(static_cast<uint32_t>(devices.size()));
    for (size_t i = 0; i < devices.size(); ++i) {
        result[static_cast<uint32_t>(i)] = Core::PlaybackDeviceInfo{
            to_hstring(devices[i].address),
            to_hstring(devices[i].name),
            devices[i].playing,
        };
    }
    return result;
}

// =========================================================================
// COMMANDS
// =========================================================================

IAsyncAction HeadsetClient::ConnectAsync(hstring bluetoothAddress) {
    std::string address = to_string(bluetoothAddress);
    return RunAsync([address](sony::protocol::HeadsetController& controller) { controller.connect(address); });
}

void HeadsetClient::Disconnect() {
    m_controller->disconnect();
}

IAsyncAction HeadsetClient::RefreshBatteryAsync() {
    return RunAsync([](sony::protocol::HeadsetController& controller) { controller.refreshBattery(); });
}

IAsyncAction HeadsetClient::SetNoiseControlAsync(Core::NoiseControlInfo value) {
    const sony::protocol::NoiseControlState state{
        .mode = fromNoiseMode(value.Mode),
        .ambientLevel = value.AmbientLevel,
        .focusOnVoice = value.FocusOnVoice,
    };
    return RunAsync([state](sony::protocol::HeadsetController& controller) { controller.setNoiseControl(state); });
}

IAsyncAction HeadsetClient::SetEqualizerPresetAsync(int32_t preset) {
    return RunAsync([preset](sony::protocol::HeadsetController& controller) { controller.setEqualizerPreset(preset); });
}

IAsyncAction HeadsetClient::SetEqualizerCustomAsync(Core::EqualizerInfo value) {
    const std::array<int, 5> bands{value.Band1, value.Band2, value.Band3, value.Band4, value.Band5};
    const int clearBass = value.ClearBass;
    return RunAsync([clearBass, bands](sony::protocol::HeadsetController& controller) { controller.setEqualizerCustom(clearBass, bands); });
}

IAsyncAction HeadsetClient::PowerOffAsync() {
    return RunAsync([](sony::protocol::HeadsetController& controller) { controller.powerOff(); });
}

IAsyncAction HeadsetClient::SetConnectionQualityAsync(bool prioritizeStableConnection) {
    return RunAsync([prioritizeStableConnection](sony::protocol::HeadsetController& controller) {
        controller.setConnectionQuality(prioritizeStableConnection);
    });
}

IAsyncAction HeadsetClient::SetDseeAsync(bool enabled) {
    return RunAsync([enabled](sony::protocol::HeadsetController& controller) { controller.setDsee(enabled); });
}

IAsyncAction HeadsetClient::SetVptAsync(int32_t preset) {
    return RunAsync([preset](sony::protocol::HeadsetController& controller) { controller.setVpt(preset); });
}

IAsyncAction HeadsetClient::SetSoundPositionAsync(int32_t position) {
    return RunAsync([position](sony::protocol::HeadsetController& controller) { controller.setSoundPosition(position); });
}

IAsyncAction HeadsetClient::SetVoiceGuidanceAsync(int32_t value) {
    return RunAsync([value](sony::protocol::HeadsetController& controller) { controller.setVoiceGuidance(value); });
}

IAsyncAction HeadsetClient::SetSpeakToChatAsync(bool enabled) {
    return RunAsync([enabled](sony::protocol::HeadsetController& controller) { controller.setSpeakToChat(enabled); });
}

IAsyncAction HeadsetClient::SetAdaptiveVolumeAsync(bool enabled) {
    return RunAsync([enabled](sony::protocol::HeadsetController& controller) { controller.setAdaptiveVolume(enabled); });
}

IAsyncAction HeadsetClient::SetAutoPowerOffAsync(int32_t index) {
    return RunAsync([index](sony::protocol::HeadsetController& controller) { controller.setAutoPowerOff(index); });
}

IAsyncAction HeadsetClient::SwitchPlaybackAsync(hstring address) {
    return RunAsync([address = to_string(address)](sony::protocol::HeadsetController& controller) {
        controller.switchPlayback(address);
    });
}

IAsyncAction HeadsetClient::RunAsync(Command command) {
    auto strong = get_strong();
    auto controller = m_controller;

    co_await resume_background();

    // No C++ exception may cross the ABI: each one becomes an HRESULT.
    hresult failure{S_OK};
    hstring message;
    try {
        command(*controller);
    } catch (const sony::SonyException& ex) {
        failure = hresult{sony::protocol::toHresult(ex.code())};
        message = to_hstring(std::string_view{ex.what()});
    } catch (const std::exception& ex) {
        failure = hresult{E_FAIL};
        message = to_hstring(std::string_view{ex.what()});
    }

    if (failure != S_OK) {
        throw hresult_error(failure, message);
    }
}

// =========================================================================
// EVENTS
// =========================================================================

event_token HeadsetClient::StateChanged(TypedEventHandler<Core::HeadsetClient, Core::HeadsetState> const& handler) {
    HookControllerCallbacks();
    return m_stateChanged.add(handler);
}

void HeadsetClient::StateChanged(event_token const& token) noexcept {
    m_stateChanged.remove(token);
}

event_token HeadsetClient::Disconnected(TypedEventHandler<Core::HeadsetClient, IInspectable> const& handler) {
    HookControllerCallbacks();
    return m_disconnected.add(handler);
}

void HeadsetClient::Disconnected(event_token const& token) noexcept {
    m_disconnected.remove(token);
}


void HeadsetClient::Close() {
    m_controller->disconnect();
}

} // namespace winrt::ConnectFlyout::Core::implementation
