#pragma once

#include "SemanticTypes.h"
#include "sony/transport/SonyError.h"
#include <array>
#include <string>
#include <vector>

namespace sony::protocol {

enum class ProtocolGeneration {
    V1,
    V2
};

class IProtocol {
public:
    virtual ~IProtocol() = default;

    [[nodiscard]] virtual ProtocolGeneration generation() const noexcept = 0;

    virtual void initDevice() = 0;

    virtual BatteryState getBattery() = 0;

    virtual NoiseControlState getNoiseControl() = 0;
    virtual void setNoiseControl(const NoiseControlState& state) = 0;

    virtual EqualizerState getEqualizer() = 0;
    // ultMode is the ULT-series mode byte the headset last reported; protocols without one ignore it.
    virtual void setEqualizerPreset(int preset, uint8_t ultMode) = 0;
    virtual void setEqualizerCustom(int clearBass, const std::array<int, 5>& bands, uint8_t ultMode) = 0;

    virtual bool getDsee() = 0;
    virtual void setDsee(bool enabled) = 0;

    virtual std::string getFirmwareVersion() = 0;
    virtual std::string getCodec() = 0;

    virtual int getAutoPowerOff() = 0;
    virtual void setAutoPowerOff(int index) = 0;

    // Power Off
    // The Headset May Drop The Link Before Acknowledging
    virtual void powerOff() = 0;

    virtual bool getSpeakToChat() = 0;
    virtual void setSpeakToChat(bool enabled) = 0;

    virtual bool getAdaptiveVolume() = 0;
    virtual void setAdaptiveVolume(bool enabled) = 0;

    // Multipoint: devices connected to the headset and switching playback between them.
    // Generations without it throw Unsupported.
    virtual std::vector<PlaybackDevice> getPlaybackDevices() {
        throw SonyException(SonyErrorCode::Unsupported, "Playback switching isn't supported");
    }
    virtual void switchPlayback(const std::string& /*address*/) {
        throw SonyException(SonyErrorCode::Unsupported, "Playback switching isn't supported");
    }

    // Connection Quality
    // Appended To Preserve Existing Virtual Slots
    virtual int getConnectionQuality() {
        throw SonyException(SonyErrorCode::Unsupported, "Connection quality isn't supported");
    }
    virtual void setConnectionQuality(bool /*prioritizeStableConnection*/) {
        throw SonyException(SonyErrorCode::Unsupported, "Connection quality isn't supported");
    }
};

} // namespace sony::protocol
