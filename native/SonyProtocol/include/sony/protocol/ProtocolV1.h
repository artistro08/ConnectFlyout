#pragma once

#include "IProtocol.h"
#include "SonyProtocolSession.h"

namespace sony::protocol {

// Legacy command set spoken by WH-1000XM3/XM4 and their contemporaries.
//
// Critical: opcode 0x22 is POWER OFF on this generation (it is BATTERY GET on
// V2). Nothing in this class may ever emit it; the battery lives behind
// 0x10/0x11 instead.
class ProtocolV1 : public IProtocol {
public:
    explicit ProtocolV1(SonyProtocolSession& session);
    ProtocolV1(SonyProtocolSession& session, bool whXb900nLayout);
    ~ProtocolV1() override = default;

    [[nodiscard]] ProtocolGeneration generation() const noexcept override {
        return ProtocolGeneration::V1;
    }

    void initDevice() override;

    BatteryState getBattery() override;

    NoiseControlState getNoiseControl() override;
    void setNoiseControl(const NoiseControlState& state) override;

    EqualizerState getEqualizer() override;
    void setEqualizerPreset(int preset, uint8_t ultMode) override;
    void setEqualizerCustom(int clearBass, const std::array<int, 5>& bands, uint8_t ultMode) override;

    bool getDsee() override;
    void setDsee(bool enabled) override;
    void powerOff() override;

    int getConnectionQuality() override;
    void setConnectionQuality(bool prioritizeStableConnection) override;
    std::string getFirmwareVersion() override;
    std::string getCodec() override;

    int getAutoPowerOff() override;
    void setAutoPowerOff(int index) override;

    bool getSpeakToChat() override;
    void setSpeakToChat(bool enabled) override;

    bool getAdaptiveVolume() override;
    void setAdaptiveVolume(bool enabled) override;

    // V1-specific surround & positioning commands
    int getVpt();
    void setVpt(int preset);
    int getSoundPosition();
    void setSoundPosition(int preset);
    int getVoiceGuidance();
    void setVoiceGuidance(int value);

private:
    SonyProtocolSession& _session;
    bool _whXb900nLayout = false;
    uint8_t _whXb900nLastTimedAutoPowerOffCode = 0x00;
};

} // namespace sony::protocol
