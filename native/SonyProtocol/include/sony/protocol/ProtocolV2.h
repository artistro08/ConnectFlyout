#pragma once

#include "IProtocol.h"
#include "SonyProtocolSession.h"
#include <mutex>
#include <optional>

namespace sony::protocol {

class ProtocolV2 : public IProtocol {
public:
    // earbuds: ask for left/right and case battery before the single-battery query.
    // ultEqualizer: read/write the equalizer as inquired type 0x03 (ULT series).
    explicit ProtocolV2(SonyProtocolSession& session, bool earbuds = false, bool ultEqualizer = false);
    ~ProtocolV2() override = default;

    [[nodiscard]] ProtocolGeneration generation() const noexcept override {
        return ProtocolGeneration::V2;
    }

    void initDevice() override;

    // V2 uses opcode 0x22 for battery inquiries
    BatteryState getBattery() override;

    NoiseControlState getNoiseControl() override;
    void setNoiseControl(const NoiseControlState& state) override;

    EqualizerState getEqualizer() override;
    void setEqualizerPreset(int preset, uint8_t ultMode) override;
    void setEqualizerCustom(int clearBass, const std::array<int, 5>& bands, uint8_t ultMode) override;

    bool getDsee() override;
    void setDsee(bool enabled) override;
    void powerOff() override;

    std::string getFirmwareVersion() override;
    std::string getCodec() override;

    int getAutoPowerOff() override;
    void setAutoPowerOff(int index) override;

    bool getSpeakToChat() override;
    void setSpeakToChat(bool enabled) override;

    bool getAdaptiveVolume() override;
    void setAdaptiveVolume(bool enabled) override;

    // Multipoint, over the second command table (DataMdrNo2). Throws Unsupported when the
    // headset's support list leaves out source switching.
    std::vector<PlaybackDevice> getPlaybackDevices() override;
    // Waits for the headset's answer; throws InvalidResponse when it refuses (on a call).
    void switchPlayback(const std::string& address) override;

private:
    [[nodiscard]] uint8_t eqInquiredType() const noexcept;

    SonyProtocolSession& _session;
    std::mutex _mutex;
    bool _earbuds{false};
    bool _ultEqualizer{false};
    // Noise control type the headset answered: 0x19 (WF-1000XM6) or 0x17 (upstream). 0 = not read yet.
    uint8_t _ncAsmType{0};
    int _lastAmbientLevel{10};
    // Paired-device list type the headset supports (00 or 02), once its T2 support list
    // has been read; nothing = not read yet, 0xff = no source switching.
    std::optional<uint8_t> _pairedDevicesType;
};

} // namespace sony::protocol
