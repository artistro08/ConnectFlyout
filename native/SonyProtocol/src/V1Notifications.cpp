#include "sony/protocol/V1Notifications.h"
#include "sony/protocol/EqualizerPresets.h"
#include "ProtocolHelpers.h"

namespace sony::protocol {

namespace {

constexpr uint8_t kBatterySingle = 0x00;
constexpr uint8_t kNcAsmInquired = 0x02;
constexpr uint8_t kEqInquired = 0x01;
constexpr uint8_t kDualSingleOff = 0x00;

bool applyBattery(std::span<const uint8_t> payload, DeviceState& state) {
    if (payload.size() < 4 || payload[1] != kBatterySingle) {
        return false;
    }
    state.battery.main = static_cast<int>(payload[2]);
    state.battery.charging = payload[3] == 1;
    return true;
}

bool applyNoiseControl(std::span<const uint8_t> payload, DeviceState& state) {
    if (payload.size() < 8 || payload[1] != kNcAsmInquired) {
        return false;
    }
    const bool on = payload[2] != 0;
    const bool ambient = payload[4] == kDualSingleOff;

    if (!on) {
        state.noiseControl.mode = NoiseControlMode::Off;
    } else if (ambient) {
        state.noiseControl.mode = NoiseControlMode::Ambient;
    } else {
        state.noiseControl.mode = NoiseControlMode::NoiseCancelling;
    }
    state.noiseControl.ambientLevel = state.noiseControl.mode == NoiseControlMode::Ambient ? static_cast<int>(payload[7]) : 0;
    state.noiseControl.focusOnVoice = payload[6] == 1;
    return true;
}

bool applyEqualizer(std::span<const uint8_t> payload, DeviceState& state) {
    if (payload.size() < 10 || payload[1] != kEqInquired) {
        return false;
    }
    state.equalizer.preset = normalizeEqualizerPreset(static_cast<int>(payload[2]));
    state.equalizer.clearBass = static_cast<int>(payload[4]) - 10;
    for (size_t i = 0; i < state.equalizer.bands.size(); ++i) {
        state.equalizer.bands[i] = static_cast<int>(payload[5 + i]) - 10;
    }
    return true;
}

} // namespace

bool applyV1Notification(
    std::span<const uint8_t> payload,
    DeviceState& state,
    bool allowWhXb900nCodecNotification) {
    if (payload.empty()) {
        return false;
    }

    switch (payload[0]) {
        case 0x11:
        case 0x13:
            return applyBattery(payload, state);
        case 0x67:
        case 0x69:
            return applyNoiseControl(payload, state);
        case 0x57:
        case 0x59:
            return applyEqualizer(payload, state);
        case 0x1B: {
            if (!allowWhXb900nCodecNotification) {
                return false;
            }

            // WH-XB900N live Bluetooth codec notification:
            // 1B 00 <codec identifier>
            if (payload.size() < 3 || payload[1] != 0x00) {
                return false;
            }

            return detail::applyCodec(payload[2], state.codec);
        }
        default:
            return false;
    }
}

} // namespace sony::protocol
