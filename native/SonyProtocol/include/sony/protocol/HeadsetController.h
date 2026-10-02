#pragma once

#include "DeviceEventDispatcher.h"
#include "DeviceProfile.h"
#include "DeviceState.h"
#include "IProtocol.h"
#include "SonyProtocolSession.h"
#include "sony/transport/ITransport.h"

#include <array>
#include <atomic>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <string_view>

namespace sony::protocol {

// Owns one headset connection: the session, the command set for its protocol
// generation, and the last state the headset confirmed.
//
// Every call blocks the calling thread; the WinRT wrapper runs them on the
// thread pool. A command that times out is retried once before the timeout is
// reported. State only changes after the headset acknowledges a command or
// sends a notification, so callers can revert their UI to state() on failure.
class HeadsetController {
public:
    using StateCallback = std::function<void(const DeviceState&)>;
    using DisconnectedCallback = std::function<void()>;

    HeadsetController(std::unique_ptr<transport::ITransport> transport, std::string_view deviceName);
    ~HeadsetController();

    HeadsetController(const HeadsetController&) = delete;
    HeadsetController& operator=(const HeadsetController&) = delete;

    // Opens the link, runs the protocol handshake and reads the initial state.
    // Throws SonyException; the link is closed again on failure.
    void connect(const transport::DeviceAddress& address);
    void disconnect() noexcept;
    [[nodiscard]] bool isConnected() const noexcept;

    [[nodiscard]] const DeviceProfile& profile() const noexcept;
    [[nodiscard]] ProtocolGeneration generation() const noexcept;
    [[nodiscard]] DeviceState state() const;

    void refreshBattery();
    void setNoiseControl(const NoiseControlState& value);
    void setEqualizerPreset(int preset);
    void setEqualizerCustom(int clearBass, const std::array<int, 5>& bands);
    void setConnectionQuality(bool prioritizeStableConnection);
    void setDsee(bool enabled);
    void setVpt(int preset);
    void setSoundPosition(int preset);
    void setVoiceGuidance(int value);
    // Turns the headset off. The link drops afterwards, like any other disconnect.
    void powerOff();
    void setSpeakToChat(bool enabled);
    void setAdaptiveVolume(bool enabled);
    void setAutoPowerOff(int index);
    // Multipoint: moves playback to the connected device with this address. Throws
    // InvalidResponse when the headset refuses (on a call, say).
    void switchPlayback(const std::string& address);

    // Called after every confirmed state change. Runs on the calling thread for
    // commands and on the session reader thread for notifications.
    void onStateChanged(StateCallback callback);

    // Called on the session reader thread when the link drops unexpectedly.
    void onDisconnected(DisconnectedCallback callback);

private:
    template <typename Operation>
    auto withRetry(Operation&& operation) -> decltype(operation());

    template <typename Read>
    void readOptional(std::string_view feature, bool supported, Read&& read);

    // Runs a setter with one retry; a second timeout drops the link so it gets reopened.
    template <typename Operation>
    void command(Operation&& operation);
    void dropLink() noexcept;

    void throwIfDisconnectedSince(uint64_t generation) const;
    void initializeProtocolForConnection(uint64_t disconnectGeneration);
    void createProtocol(ProtocolGeneration generation);
    void detectGeneration();
    void readInitialState();
    void handleNotification(const SonyFrame& frame);
    [[nodiscard]] uint8_t currentUltMode() const;
    // Call with _stateMutex held.
    bool applyV2Notification(const std::vector<uint8_t>& payload);
    // Call with _stateMutex held. Second command table (DataMdrNo2): the multipoint list.
    bool applyTable2Notification(const std::vector<uint8_t>& payload);
    void updateState(const std::function<void(DeviceState&)>& mutation);
    void publish(const DeviceState& snapshot);

    DeviceProfile _profile;
    std::unique_ptr<SonyProtocolSession> _session;
    std::unique_ptr<IProtocol> _protocol;
    std::atomic<ProtocolGeneration> _generation{ProtocolGeneration::V1};
    DeviceEventDispatcher _dispatcher;

    std::mutex _connectMutex;
    std::atomic<uint64_t> _disconnectGeneration{0};

    mutable std::mutex _stateMutex;
    DeviceState _state;

    std::mutex _callbackMutex;
    StateCallback _stateCallback;
    DisconnectedCallback _disconnectedCallback;
};

} // namespace sony::protocol
