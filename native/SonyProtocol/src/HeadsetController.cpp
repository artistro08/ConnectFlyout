#include "sony/protocol/HeadsetController.h"

#include "sony/protocol/DeviceProfileRegistry.h"
#include "sony/protocol/EqualizerPresets.h"
#include "sony/protocol/ProtocolV1.h"
#include "sony/protocol/ProtocolV2.h"
#include "sony/protocol/V1Notifications.h"
#include "sony/protocol/V2Layouts.h"
#include "sony/transport/Logger.h"

#include <string>

namespace sony::protocol {

namespace {

constexpr std::string_view kCategory = LogCategory::Device;

// Unknown Sony models only get the features every generation shares.
DeviceCapabilities unknownModelCapabilities() noexcept {
    DeviceCapabilities capabilities;
    capabilities.battery = true;
    capabilities.noiseCancelling = true;
    capabilities.ambientSound = true;
    capabilities.focusOnVoice = true;
    capabilities.powerOff = true;
    capabilities.autoPowerOffWhenRemoved = true;
    return capabilities;
}

} // namespace

HeadsetController::HeadsetController(std::unique_ptr<transport::ITransport> transport, std::string_view deviceName)
    : _profile(DeviceProfileRegistry::getProfileForDevice(deviceName)),
      _session(std::make_unique<SonyProtocolSession>(std::move(transport))) {
    // Unknown models start on V2 and fall back to V1 during connect. WH-XB900N
    // also starts on V2 because firmware 4.5.2 expects the common init handshake
    // before its legacy V1 command set is used.
    if (_profile.model == SonyModel::Unknown) {
        _profile.capabilities = unknownModelCapabilities();
        createProtocol(ProtocolGeneration::V2);
    } else if (_profile.model == SonyModel::WHXB900N) {
        createProtocol(ProtocolGeneration::V2);
    } else {
        createProtocol(_profile.protocol == SonyProtocolVersion::V2 ? ProtocolGeneration::V2 : ProtocolGeneration::V1);
    }

    _session->onNotification([this](const SonyFrame& frame) { handleNotification(frame); });
    _session->onDisconnected([this] {
        DisconnectedCallback callback;
        {
            std::lock_guard lock(_callbackMutex);
            callback = _disconnectedCallback;
        }
        if (callback) {
            callback();
        }
    });
}

HeadsetController::~HeadsetController() {
    disconnect();
}

void HeadsetController::connect(const transport::DeviceAddress& address) {
    // One connect at a time; a disconnect() while this runs cancels it.
    std::lock_guard connectLock(_connectMutex);
    const uint64_t generation = _disconnectGeneration.load();

    // Address only at debug: logs get attached to bug reports
    Logger::info(kCategory, "Connecting to " + std::string(to_string(_profile.model)));
    Logger::debug(kCategory, "Headset address " + address.str());
    _session->connect(address);

    try {
        throwIfDisconnectedSince(generation);
        initializeProtocolForConnection(generation);
        readInitialState();
        throwIfDisconnectedSince(generation);

        // Optional reads shrug off errors, so check the headset didn't drop the link meanwhile
        if (!_session->isConnected()) {
            throw SonyException(SonyErrorCode::Disconnected, "Link dropped while connecting");
        }
    } catch (...) {
        _session->disconnect();
        throw;
    }

    Logger::info(kCategory, "Connected to " + std::string(to_string(_profile.model)));
}

void HeadsetController::disconnect() noexcept {
    ++_disconnectGeneration;
    _session->disconnect();
}

void HeadsetController::throwIfDisconnectedSince(uint64_t generation) const {
    if (_disconnectGeneration.load() != generation) {
        throw SonyException(SonyErrorCode::Disconnected, "Disconnected while connecting");
    }
}

bool HeadsetController::isConnected() const noexcept {
    return _session->isConnected();
}

const DeviceProfile& HeadsetController::profile() const noexcept {
    return _profile;
}

ProtocolGeneration HeadsetController::generation() const noexcept {
    return _generation.load();
}

DeviceState HeadsetController::state() const {
    std::lock_guard lock(_stateMutex);
    return _state;
}

void HeadsetController::refreshBattery() {
    const BatteryState battery = withRetry([&] { return _protocol->getBattery(); });
    updateState([&](DeviceState& state) { state.battery = battery; });
}

void HeadsetController::setNoiseControl(const NoiseControlState& value) {
    command([&] { _protocol->setNoiseControl(value); });
    updateState([&](DeviceState& state) {
        state.noiseControl = value;
        if (value.mode != NoiseControlMode::Ambient) {
            state.noiseControl.ambientLevel = 0;
        }
    });
}

uint8_t HeadsetController::currentUltMode() const {
    std::lock_guard lock(_stateMutex);
    return _state.equalizer.ultMode;
}

void HeadsetController::setEqualizerPreset(int preset) {
    command([&] { _protocol->setEqualizerPreset(preset, currentUltMode()); });
    updateState([&](DeviceState& state) { state.equalizer.preset = preset; });
}

void HeadsetController::setEqualizerCustom(int clearBass, const std::array<int, 5>& bands) {
    command([&] { _protocol->setEqualizerCustom(clearBass, bands, currentUltMode()); });
    updateState([&](DeviceState& state) {
        state.equalizer.preset = static_cast<int>(EqualizerPreset::Manual);
        state.equalizer.clearBass = clearBass;
        state.equalizer.bands = bands;
    });
}

void HeadsetController::powerOff() {
    // The headset can switch off before it acknowledges; that's the goal, not a failure,
    // so no retry and no "stopped answering" handling here
    try {
        _protocol->powerOff();
    } catch (const SonyException& ex) {
        if (ex.code() != SonyErrorCode::Timeout && ex.code() != SonyErrorCode::Disconnected) {
            throw;
        }
    }
}

void HeadsetController::setConnectionQuality(bool prioritizeStableConnection) {
    if (!_profile.capabilities.connectionQuality) {
        throw SonyException(SonyErrorCode::Unsupported, "Connection quality is not supported by this headset");
    }
    command([&] { _protocol->setConnectionQuality(prioritizeStableConnection); });
    updateState([&](DeviceState& state) { state.connectionQuality = prioritizeStableConnection ? 1 : 0; });
}

void HeadsetController::setDsee(bool enabled) {
    command([&] { _protocol->setDsee(enabled); });
    updateState([&](DeviceState& state) { state.dsee = enabled; });
}

void HeadsetController::setVpt(int preset) {
    auto* v1 = dynamic_cast<ProtocolV1*>(_protocol.get());
    if (!_profile.capabilities.vpt || !v1) {
        throw SonyException(SonyErrorCode::Unsupported, "VPT is not supported by this headset");
    }
    command([&] { v1->setVpt(preset); });
    updateState([&](DeviceState& state) {
        state.vpt = preset;
        if (preset != 0) {
            state.soundPosition = 0;
        }
    });
}

void HeadsetController::setSoundPosition(int preset) {
    auto* v1 = dynamic_cast<ProtocolV1*>(_protocol.get());
    if (!_profile.capabilities.soundPosition || !v1) {
        throw SonyException(SonyErrorCode::Unsupported, "Sound position is not supported by this headset");
    }
    command([&] { v1->setSoundPosition(preset); });
    updateState([&](DeviceState& state) {
        state.soundPosition = preset;
        if (preset != 0) {
            state.vpt = 0;
        }
    });
}

void HeadsetController::setVoiceGuidance(int value) {
    auto* v1 = dynamic_cast<ProtocolV1*>(_protocol.get());
    if (!_profile.capabilities.voiceGuidance || !v1) {
        throw SonyException(SonyErrorCode::Unsupported, "Voice guidance is not supported by this headset");
    }
    command([&] { v1->setVoiceGuidance(value); });
    updateState([&](DeviceState& state) { state.voiceGuidance = value; });
}

void HeadsetController::setSpeakToChat(bool enabled) {
    command([&] { _protocol->setSpeakToChat(enabled); });
    updateState([&](DeviceState& state) { state.speakToChat = enabled; });
}

void HeadsetController::setAdaptiveVolume(bool enabled) {
    command([&] { _protocol->setAdaptiveVolume(enabled); });
    updateState([&](DeviceState& state) { state.adaptiveVolume = enabled; });
}

void HeadsetController::setAutoPowerOff(int index) {
    command([&] { _protocol->setAutoPowerOff(index); });
    updateState([&](DeviceState& state) { state.autoPowerOff = index; });
}

void HeadsetController::switchPlayback(const std::string& address) {
    command([&] { _protocol->switchPlayback(address); });
    updateState([&](DeviceState& state) { markPlaying(state.playbackDevices, address); });
}

void HeadsetController::onStateChanged(StateCallback callback) {
    std::lock_guard lock(_callbackMutex);
    _stateCallback = std::move(callback);
}

void HeadsetController::onDisconnected(DisconnectedCallback callback) {
    std::lock_guard lock(_callbackMutex);
    _disconnectedCallback = std::move(callback);
}

template <typename Operation>
auto HeadsetController::withRetry(Operation&& operation) -> decltype(operation()) {
    try {
        return operation();
    } catch (const SonyException& ex) {
        if (ex.code() != SonyErrorCode::Timeout) {
            throw;
        }
        Logger::warn(kCategory, std::string("Command timed out, retrying once: ") + ex.what());
    }
    return operation();
}

template <typename Operation>
void HeadsetController::command(Operation&& operation) {
    try {
        withRetry(operation);
    } catch (const SonyException& ex) {
        if (ex.code() == SonyErrorCode::Timeout) {
            dropLink();
        }
        throw;
    }
}

void HeadsetController::dropLink() noexcept {
    // A real XM6 once stopped ACKing everything on one link while a fresh link worked.
    Logger::warn(kCategory, "Headset stopped answering, reopening the link");
    ++_disconnectGeneration;
    _session->disconnect();

    DisconnectedCallback callback;
    {
        std::lock_guard lock(_callbackMutex);
        callback = _disconnectedCallback;
    }
    if (callback) {
        try {
            callback();
        } catch (...) {
            Logger::error(kCategory, "Disconnected callback threw");
        }
    }
}

template <typename Read>
void HeadsetController::readOptional(std::string_view feature, bool supported, Read&& read) {
    if (!supported) {
        return;
    }
    try {
        withRetry(read);
    } catch (const SonyException& ex) {
        Logger::warn(kCategory, "Couldn't read " + std::string(feature) + ": " + ex.what());
    }
}

void HeadsetController::initializeProtocolForConnection(uint64_t disconnectGeneration) {
    if (_profile.model == SonyModel::WHXB900N) {
        // Firmware 4.5.2 expects the common Sony init handshake before accepting
        // its legacy V1 command family. A successful connection leaves the active
        // protocol on V1, so recreate V2 first on every reconnect.
        createProtocol(ProtocolGeneration::V2);
        _protocol->initDevice();
        throwIfDisconnectedSince(disconnectGeneration);

        createProtocol(ProtocolGeneration::V1);
        _protocol->initDevice();
        return;
    }

    _protocol->initDevice();
    if (_profile.model == SonyModel::Unknown) {
        detectGeneration();
    }
}

void HeadsetController::createProtocol(ProtocolGeneration generation) {
    if (generation == ProtocolGeneration::V2) {
        _protocol = std::make_unique<ProtocolV2>(*_session, _profile.capabilities.dualBattery,
                                                _profile.capabilities.ultEqualizer);
    } else {
        _protocol = std::make_unique<ProtocolV1>(*_session, _profile.model == SonyModel::WHXB900N);
    }
    _generation.store(generation);
}

void HeadsetController::detectGeneration() {
    // Noise control GET is harmless on both generations. Never probe with the
    // V2 battery opcode: 0x22 powers a V1 headset off.
    try {
        (void)withRetry([&] { return _protocol->getNoiseControl(); });
    } catch (const SonyException&) {
        Logger::info(kCategory, "Unknown model didn't answer V2 commands, trying V1");
        createProtocol(ProtocolGeneration::V1);
        _protocol->initDevice();
    }
}

void HeadsetController::readInitialState() {
    const DeviceCapabilities& capabilities = _profile.capabilities;
    DeviceState initial;

    initial.battery = withRetry([&] { return _protocol->getBattery(); });
    initial.noiseControl = withRetry([&] { return _protocol->getNoiseControl(); });

    readOptional("equalizer", capabilities.equalizer, [&] { initial.equalizer = _protocol->getEqualizer(); });
    readOptional("connection quality", capabilities.connectionQuality, [&] {
        initial.connectionQuality = _protocol->getConnectionQuality();
    });
    readOptional("voice guidance", capabilities.voiceGuidance, [&] {
        auto* v1 = dynamic_cast<ProtocolV1*>(_protocol.get());
        if (v1) {
            initial.voiceGuidance = v1->getVoiceGuidance();
        }
    });
    readOptional("VPT", capabilities.vpt, [&] {
        auto* v1 = dynamic_cast<ProtocolV1*>(_protocol.get());
        if (v1) {
            initial.vpt = v1->getVpt();
        }
    });
    readOptional("sound position", capabilities.soundPosition, [&] {
        auto* v1 = dynamic_cast<ProtocolV1*>(_protocol.get());
        if (v1) {
            initial.soundPosition = v1->getSoundPosition();
        }
    });
    readOptional("DSEE", capabilities.dsee, [&] { initial.dsee = _protocol->getDsee(); });
    readOptional("Speak-to-Chat", capabilities.speakToChat, [&] { initial.speakToChat = _protocol->getSpeakToChat(); });
    readOptional("adaptive volume", capabilities.adaptiveVolume, [&] { initial.adaptiveVolume = _protocol->getAdaptiveVolume(); });
    readOptional("auto power-off", capabilities.autoPowerOff, [&] { initial.autoPowerOff = _protocol->getAutoPowerOff(); });
    readOptional("firmware", capabilities.firmwareInfo, [&] { initial.firmware = _protocol->getFirmwareVersion(); });
    readOptional("codec", capabilities.codecInfo, [&] { initial.codec = _protocol->getCodec(); });

    readOptional("playback devices", capabilities.multipoint && _generation.load() == ProtocolGeneration::V2, [&] {
        initial.playbackDevices = _protocol->getPlaybackDevices();
    });

    {
        std::lock_guard lock(_stateMutex);
        _state = initial;
    }
    publish(initial);
}

void HeadsetController::handleNotification(const SonyFrame& frame) {
    bool handled = false;
    DeviceState snapshot;
    {
        std::lock_guard lock(_stateMutex);
        if (frame.type == DataType::DataMdrNo2) {
            handled = applyTable2Notification(frame.payload);
        } else {
            handled = _generation.load() == ProtocolGeneration::V2
                ? applyV2Notification(frame.payload)
                : applyV1Notification(
                    frame.payload,
                    _state,
                    _profile.model == SonyModel::WHXB900N);
        }
        if (handled) {
            snapshot = _state;
        }
    }

    if (!handled) {
        Logger::debug(kCategory, "Ignored notification: " + Logger::formatHex(frame.payload));
        return;
    }
    publish(snapshot);
}

bool HeadsetController::applyV2Notification(const std::vector<uint8_t>& payload) {
    // Layouts upstream's dispatcher doesn't know (WF-1000XM6) first, then upstream's.
    if (payload.size() >= 2 && (payload[0] == 0x67 || payload[0] == 0x69) && payload[1] == kNcAsmSeamless) {
        return parseNcAsmSeamless(payload, _state.noiseControl);
    }
    if (!payload.empty() && (payload[0] == 0x57 || payload[0] == 0x59)) {
        return parseEqualizer(payload, _state.equalizer);
    }
    return _dispatcher.parseNotificationPayload(payload, _state, false);
}

bool HeadsetController::applyTable2Notification(const std::vector<uint8_t>& payload) {
    // A device connected, left or took over playback
    if (auto devices = parsePlaybackDevices(payload)) {
        _state.playbackDevices = std::move(*devices);
        return true;
    }
    // A switch finished, maybe started from the phone
    if (const auto result = parsePlaybackSwitch(payload); result && result->succeeded) {
        markPlaying(_state.playbackDevices, result->address);
        return true;
    }
    return false;
}

void HeadsetController::updateState(const std::function<void(DeviceState&)>& mutation) {
    DeviceState snapshot;
    {
        std::lock_guard lock(_stateMutex);
        mutation(_state);
        snapshot = _state;
    }
    publish(snapshot);
}

void HeadsetController::publish(const DeviceState& snapshot) {
    StateCallback callback;
    {
        std::lock_guard lock(_callbackMutex);
        callback = _stateCallback;
    }
    if (!callback) {
        return;
    }
    try {
        callback(snapshot);
    } catch (...) {
        Logger::error(kCategory, "State callback threw");
    }
}

} // namespace sony::protocol
