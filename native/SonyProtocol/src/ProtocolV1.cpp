#include "sony/protocol/ProtocolV1.h"
#include "ProtocolHelpers.h"
#include "sony/protocol/EqualizerPresets.h"

#include <algorithm>
#include <chrono>

// Byte layouts below match Client/CommandSerializer.cpp (the legacy client,
// exercised on WH-1000XM3 hardware for years) and Gadgetbridge's
// SonyProtocolImplV1. Every GET was additionally replayed against a
// WH-1000XM4 on firmware 3.0.1 before being trusted here.
//
// CRITICAL: opcode 0x22 is POWER OFF on this generation. Only powerOff() may
// send it; Xm4Tests pins that the battery path never does.

namespace sony::protocol {

using detail::clampEqValue;
using detail::codecName;

namespace {

constexpr uint8_t kNcAsmInquired = 0x02;  // NOISE_CANCELLING_AND_AMBIENT_SOUND_MODE
constexpr uint8_t kEqInquired = 0x01;     // PRESET_EQ

constexpr uint8_t kEffectOff = 0x00;
constexpr uint8_t kEffectAdjustmentCompletion = 0x11;
constexpr uint8_t kLevelAdjustment = 0x01;
constexpr uint8_t kDualSingleOff = 0x00;   // ambient sound passthrough
constexpr uint8_t kDualSingleDual = 0x02;  // noise cancelling

int whXb900nAutoPowerOffIndex(uint8_t code) {
    switch (code) {
        case 0x11: return 0; // disabled
        case 0x00: return 1; // 5 minutes
        case 0x01: return 2; // 30 minutes
        case 0x02: return 3; // 1 hour
        case 0x03: return 4; // 3 hours
        default: return -1;
    }
}

uint8_t whXb900nAutoPowerOffCode(int index) {
    switch (index) {
        case 1: return 0x00;
        case 2: return 0x01;
        case 3: return 0x02;
        case 4: return 0x03;
        default: return 0x11;
    }
}

bool isWhXb900nSoundPositionCode(uint8_t code) {
    switch (code) {
        case 0x00:
        case 0x01:
        case 0x02:
        case 0x03:
        case 0x11:
        case 0x12:
            return true;
        default:
            return false;
    }
}

constexpr auto kTimeout = std::chrono::milliseconds(1000);

} // namespace

ProtocolV1::ProtocolV1(SonyProtocolSession& session)
    : ProtocolV1(session, false) {}

ProtocolV1::ProtocolV1(SonyProtocolSession& session, bool whXb900nLayout)
    : _session(session), _whXb900nLayout(whXb900nLayout) {}

void ProtocolV1::initDevice() {
    // V1 does not require an init handshake like V2; best-effort poll of
    // ambient state. The first reply after connecting is the slow one on a
    // WH-1000XM4 (well past 500 ms), and giving up early leaves its late ACK
    // and data frame to collide with the next request, so wait as long as
    // any other query does.
    try {
        _session.sendAndAwaitResponse(
            SonyFrame{ .type = DataType::DataMdr, .payload = {0x66, kNcAsmInquired} },
            0x67,
            kNcAsmInquired,
            std::chrono::milliseconds(1500)
        );
    } catch (const SonyException&) {}
}

BatteryState ProtocolV1::getBattery() {
    // GET 10 <type> -> RET 11 <type> ..., type 0x00 single, 0x01 dual (L/R),
    // 0x02 case. Over-ear models answer every type and echo the single reading
    // into the left slot, so the single reading is authoritative when it
    // arrives and dual/case are only consulted for earbuds.
    BatteryState state;

    // 1. Single battery: GET 10 00 -> RET 11 00 <level> <charging>
    try {
        auto resp = _session.sendAndAwaitResponse(
            SonyFrame{ .type = DataType::DataMdr, .payload = {0x10, 0x00} },
            0x11, 0x00, kTimeout);
        if (resp.payload.size() >= 4) {
            state.main = static_cast<int>(resp.payload[2]);
            state.charging = (resp.payload[3] == 1);
            return state;
        }
    } catch (const SonyException&) {}

    // 2. Dual L/R battery: GET 10 01 -> RET 11 01 <Llvl> <Lchg> <Rlvl> <Rchg>
    try {
        auto resp = _session.sendAndAwaitResponse(
            SonyFrame{ .type = DataType::DataMdr, .payload = {0x10, 0x01} },
            0x11, 0x01, kTimeout);
        if (resp.payload.size() >= 6) {
            const int left = static_cast<int>(resp.payload[2]);
            const int right = static_cast<int>(resp.payload[4]);
            state.charging = (resp.payload[3] == 1 || resp.payload[5] == 1);
            if (right == 0 && left > 0) {
                // Over-ear models answer the dual query too, echoing their
                // single cell into the left slot with an empty right one.
                // Reporting min(left, right) there would read as 0%.
                state.main = left;
            } else {
                state.left = left;
                state.right = right;
                state.main = std::min(left, right);
            }
        }
    } catch (const SonyException&) {}

    // 3. Case battery: GET 10 02 -> RET 11 02 <level> <charging>
    try {
        auto resp = _session.sendAndAwaitResponse(
            SonyFrame{ .type = DataType::DataMdr, .payload = {0x10, 0x02} },
            0x11, 0x02, kTimeout);
        if (resp.payload.size() >= 4) {
            state.caseBattery = static_cast<int>(resp.payload[2]);
        }
    } catch (const SonyException&) {}

    return state;
}

NoiseControlState ProtocolV1::getNoiseControl() {
    // GET 66 02 -> RET 67 02 <effect> <ncSettingType> <dualSingle> <asmSettingType> <asmId> <asmLevel>
    // effect 0 is off. Otherwise dualSingle selects the mode: 0 is ambient
    // sound at <asmLevel>, 1 (single) and 2 (dual) are noise cancelling.
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0x66, kNcAsmInquired} },
        0x67, kNcAsmInquired, kTimeout);

    if (resp.payload.size() < 8 || resp.payload[1] != kNcAsmInquired)
        throw SonyException(SonyErrorCode::InvalidResponse, "Malformed noise-control response");

    const bool on = resp.payload[2] != kEffectOff;
    const bool ambient = resp.payload[4] == kDualSingleOff;
    const bool voice = resp.payload[6] == 1;
    const int level = static_cast<int>(resp.payload[7]);

    NoiseControlState state;
    if (!on) {
        state.mode = NoiseControlMode::Off;
    } else if (ambient) {
        state.mode = NoiseControlMode::Ambient;
    } else {
        state.mode = NoiseControlMode::NoiseCancelling;
    }
    state.ambientLevel = (state.mode == NoiseControlMode::Ambient) ? level : 0;
    state.focusOnVoice = voice;
    return state;
}

void ProtocolV1::setNoiseControl(const NoiseControlState& state) {
    // WH-XB900N uses the V1 control payload shape observed in the compatible
    // Android client: effect 0x10 when enabled and NC setting type 0x02.
    // Keep the existing 0x11 / 0x01 format for all other V1 devices.
    const bool off = state.mode == NoiseControlMode::Off;
    const bool ambient = state.mode == NoiseControlMode::Ambient;
    const uint8_t level = ambient ? static_cast<uint8_t>(std::clamp(state.ambientLevel, 1, 20)) : 0;

    std::vector<uint8_t> payload = {
        0x68,
        kNcAsmInquired,
        static_cast<uint8_t>(off ? kEffectOff : (_whXb900nLayout ? 0x10 : kEffectAdjustmentCompletion)),
        static_cast<uint8_t>(_whXb900nLayout ? 0x02 : kLevelAdjustment),
        static_cast<uint8_t>((ambient || off) ? kDualSingleOff : (_whXb900nLayout ? 0x01 : kDualSingleDual)),
        kLevelAdjustment,
        static_cast<uint8_t>((_whXb900nLayout ? ambient && state.focusOnVoice : state.focusOnVoice) ? 1 : 0),
        level
    };

    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

EqualizerState ProtocolV1::getEqualizer() {
    // GET 56 01 -> RET 57 01 <preset> 06 <bass+10> <b1..b5 +10>
    // Same table as V2, behind inquired type 0x01 (PRESET_EQ) instead of 0x00.
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0x56, kEqInquired} },
        0x57, kEqInquired, kTimeout);

    if (resp.payload.size() < 10 || resp.payload[1] != kEqInquired)
        throw SonyException(SonyErrorCode::InvalidResponse, "Incomplete equalizer response");

    EqualizerState state;
    state.preset = normalizeEqualizerPreset(static_cast<int>(resp.payload[2]));
    state.clearBass = static_cast<int>(resp.payload[4]) - 10;
    for (size_t i = 0; i < 5; ++i) {
        state.bands[i] = static_cast<int>(resp.payload[5 + i]) - 10;
    }
    return state;
}

void ProtocolV1::setEqualizerPreset(int preset, uint8_t /*ultMode*/) {
    // SET preset: 58 01 <preset> 00
    std::vector<uint8_t> payload = {
        0x58,
        kEqInquired,
        static_cast<uint8_t>(preset),
        0x00
    };
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

void ProtocolV1::setEqualizerCustom(int clearBass, const std::array<int, 5>& bands, uint8_t /*ultMode*/) {
    // WH-XB900N write uses FF; its returned Manual preset is A0.
    // Other V1 models retain the existing A0 write format.
    std::vector<uint8_t> payload = {
        0x58,
        kEqInquired,
        static_cast<uint8_t>(_whXb900nLayout ? 0xff : 0xa0),
        0x06,
        clampEqValue(clearBass)
    };
    for (int b : bands) {
        payload.push_back(clampEqValue(b));
    }
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

bool ProtocolV1::getDsee() {
    if (!_whXb900nLayout) {
        throw SonyException(SonyErrorCode::Unsupported, "DSEE is not supported on Protocol V1");
    }
    // WH-XB900N (firmware 4.5.2), captured from Sony Sound Connect:
    // GET E6 02 -> RET E7 02 00 <00 Off / 01 Auto>.
    const auto response = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0xe6, 0x02} },
        0xe7, 0x02, kTimeout);
    if (response.payload.size() != 4 || response.payload[2] != 0x00 || response.payload[3] > 0x01) {
        throw SonyException(SonyErrorCode::InvalidResponse, "Invalid WH-XB900N DSEE state response");
    }
    return response.payload[3] == 0x01;
}

void ProtocolV1::powerOff() {
    // SET: 22 00 01 (Gadgetbridge's SonyProtocolImplV1.powerOff)
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = {0x22, 0x00, 0x01} });
}

void ProtocolV1::setDsee(bool enabled) {
    if (!_whXb900nLayout) {
        throw SonyException(SonyErrorCode::Unsupported, "DSEE is not supported on Protocol V1");
    }
    // WH-XB900N: SET E8 02 00 <00 Off / 01 Auto> -> RET E9 02 00 <same>.
    const uint8_t requested = static_cast<uint8_t>(enabled ? 0x01 : 0x00);
    const auto response = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0xe8, 0x02, 0x00, requested} },
        0xe9, 0x02, kTimeout);
    if (response.payload.size() != 4 || response.payload[2] != 0x00 || response.payload[3] != requested) {
        throw SonyException(SonyErrorCode::InvalidResponse, "WH-XB900N DSEE setting was not confirmed");
    }
}

int ProtocolV1::getConnectionQuality() {
    if (!_whXb900nLayout) {
        throw SonyException(SonyErrorCode::Unsupported, "Connection quality unsupported on this V1 model");
    }
    // Observed on WH-XB900N 4.5.2: GET E6 01 -> E7 01 00 XX.
    const auto response = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0xe6, 0x01} },
        0xe7, 0x01, kTimeout);
    if (response.payload.size() != 4 || response.payload[2] != 0 || response.payload[3] > 1) {
        throw SonyException(SonyErrorCode::InvalidResponse, "Invalid WH-XB900N connection quality response");
    }
    return response.payload[3];
}

void ProtocolV1::setConnectionQuality(bool prioritizeStableConnection) {
    if (!_whXb900nLayout) {
        throw SonyException(SonyErrorCode::Unsupported, "Connection quality unsupported on this V1 model");
    }
    // WH-XB900N firmware 4.5.2: 0 = Prioritize Sound Quality,
    // 1 = Prioritize Stable Connection.
    const uint8_t requested = static_cast<uint8_t>(prioritizeStableConnection ? 1 : 0);
    // SET E8 01 00 XX -> E9 01 00 XX (confirmed by Sony Sound Connect capture).
    const auto response = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0xe8, 0x01, 0x00, requested} },
        0xe9, 0x01, kTimeout);
    if (response.payload.size() != 4 || response.payload[2] != 0 || response.payload[3] != requested) {
        throw SonyException(SonyErrorCode::InvalidResponse, "WH-XB900N connection quality not confirmed");
    }
}
std::string ProtocolV1::getFirmwareVersion() {
    // GET 04 02 -> RET 05 02 <len> <ascii version...>
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0x04, 0x02} },
        0x05, -1, kTimeout);
    if (resp.payload.size() > 3) {
        return std::string(resp.payload.begin() + 3, resp.payload.end());
    }
    return "";
}

std::string ProtocolV1::getCodec() {
    // GET 18 00 -> RET 19 00 <codec>
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0x18, 0x00} },
        0x19, -1, kTimeout);
    if (resp.payload.size() >= 3) {
        return codecName(resp.payload[2]);
    }
    return "";
}

int ProtocolV1::getAutoPowerOff() {
    if (!_whXb900nLayout) {
        throw SonyException(SonyErrorCode::Unsupported, "Auto Power Off is not supported on Protocol V1");
    }

    // WH-XB900N: GET F6 04 -> RET F7 04 01 <current> <last timed>.
    const auto response = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0xf6, 0x04} },
        0xf7, 0x04, kTimeout);
    if (response.payload.size() != 5 || response.payload[2] != 0x01) {
        throw SonyException(SonyErrorCode::InvalidResponse, "Invalid WH-XB900N auto power-off response");
    }

    const int index = whXb900nAutoPowerOffIndex(response.payload[3]);
    if (index < 0) {
        throw SonyException(SonyErrorCode::InvalidResponse, "Unknown WH-XB900N auto power-off value");
    }

    if (response.payload[4] <= 0x03) {
        _whXb900nLastTimedAutoPowerOffCode = response.payload[4];
    } else if (index > 0) {
        _whXb900nLastTimedAutoPowerOffCode = response.payload[3];
    }
    return index;
}

void ProtocolV1::setAutoPowerOff(int index) {
    if (!_whXb900nLayout) {
        throw SonyException(SonyErrorCode::Unsupported, "Auto Power Off is not supported on Protocol V1");
    }
    if (index < 0 || index > 4) {
        throw SonyException(SonyErrorCode::Unsupported, "WH-XB900N auto power-off option is not supported");
    }

    const uint8_t current = whXb900nAutoPowerOffCode(index);
    const uint8_t lastTimed = index == 0 ? _whXb900nLastTimedAutoPowerOffCode : current;
    const auto response = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0xf8, 0x04, 0x01, current, lastTimed} },
        0xf9, 0x04, kTimeout);
    if (response.payload.size() != 5 || response.payload[2] != 0x01 ||
        response.payload[3] != current || response.payload[4] != lastTimed) {
        throw SonyException(SonyErrorCode::InvalidResponse, "WH-XB900N auto power-off setting was not confirmed");
    }

    if (index > 0) {
        _whXb900nLastTimedAutoPowerOffCode = current;
    }
}

bool ProtocolV1::getSpeakToChat() {
    throw SonyException(SonyErrorCode::Unsupported, "Speak-to-Chat is not supported on Protocol V1");
}

void ProtocolV1::setSpeakToChat(bool /*enabled*/) {
    throw SonyException(SonyErrorCode::Unsupported, "Speak-to-Chat is not supported on Protocol V1");
}

bool ProtocolV1::getAdaptiveVolume() {
    throw SonyException(SonyErrorCode::Unsupported, "Adaptive Volume is not supported on Protocol V1");
}

void ProtocolV1::setAdaptiveVolume(bool /*enabled*/) {
    throw SonyException(SonyErrorCode::Unsupported, "Adaptive Volume is not supported on Protocol V1");
}

int ProtocolV1::getVpt() {
    if (!_whXb900nLayout) {
        throw SonyException(SonyErrorCode::Unsupported, "VPT is available for WH-XB900N only");
    }

    // Hardware-verified WH-XB900N readback:
    // DataMdr GET 46 01 -> RET 47 01 <preset>.
    const auto response = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0x46, 0x01} },
        0x47, 0x01, kTimeout);
    if (response.payload.size() != 3 || response.payload[1] != 0x01 || response.payload[2] > 0x04) {
        throw SonyException(SonyErrorCode::InvalidResponse, "Invalid WH-XB900N VPT response");
    }
    return static_cast<int>(response.payload[2]);
}

void ProtocolV1::setVpt(int preset) {
    if (_whXb900nLayout && (preset < 0 || preset > 4)) {
        throw SonyException(SonyErrorCode::ProtocolViolation, "Invalid WH-XB900N VPT preset");
    }

    std::vector<uint8_t> payload = {
        0x48,
        0x01,
        static_cast<uint8_t>(preset)
    };
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

int ProtocolV1::getSoundPosition() {
    if (!_whXb900nLayout) {
        throw SonyException(SonyErrorCode::Unsupported, "Sound position is available for WH-XB900N only");
    }

    // Hardware-verified WH-XB900N readback:
    // DataMdr GET 46 02 -> RET 47 02 <position code>.
    const auto response = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0x46, 0x02} },
        0x47, 0x02, kTimeout);
    if (response.payload.size() != 3 || response.payload[1] != 0x02 ||
        !isWhXb900nSoundPositionCode(response.payload[2])) {
        throw SonyException(SonyErrorCode::InvalidResponse, "Invalid WH-XB900N sound-position response");
    }
    return static_cast<int>(response.payload[2]);
}

void ProtocolV1::setSoundPosition(int preset) {
    if (_whXb900nLayout &&
        (preset < 0 || preset > 0xff ||
         !isWhXb900nSoundPositionCode(static_cast<uint8_t>(preset)))) {
        throw SonyException(SonyErrorCode::ProtocolViolation, "Invalid WH-XB900N sound-position preset");
    }

    std::vector<uint8_t> payload = {
        0x48,
        0x02,
        static_cast<uint8_t>(preset)
    };
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

int ProtocolV1::getVoiceGuidance() {
    if (!_whXb900nLayout) {
        throw SonyException(SonyErrorCode::Unsupported, "Voice guidance is available for WH-XB900N only");
    }
    // Captured WH-XB900N firmware 4.5.2 readback:
    // DataMdrNo2 GET 46 01 01 -> RET 47 01 01 XX.
    const auto response = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdrNo2, .payload = {0x46, 0x01, 0x01} },
        0x47, 0x01, kTimeout);
    if (response.payload.size() != 4 ||
        response.payload[1] != 0x01 ||
        response.payload[2] != 0x01 ||
        response.payload[3] > 1) {
        throw SonyException(SonyErrorCode::InvalidResponse, "Invalid WH-XB900N voice-guidance response");
    }
    return static_cast<int>(response.payload[3]);
}
void ProtocolV1::setVoiceGuidance(int value) {
    if (!_whXb900nLayout || value < 0 || value > 1) {
        throw SonyException(SonyErrorCode::Unsupported, "Voice guidance is available for WH-XB900N only");
    }

    // Observed on WH-XB900N firmware 4.5.2:
    // DataMdrNo2 SET 48 01 01 XX -> RET 49 01 01 XX.
    const auto requested = static_cast<uint8_t>(value);
    const auto response = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdrNo2, .payload = {0x48, 0x01, 0x01, requested} },
        0x49, 0x01, kTimeout);
    if (response.payload.size() != 4 ||
        response.payload[1] != 0x01 ||
        response.payload[2] != 0x01 ||
        response.payload[3] != requested) {
        throw SonyException(SonyErrorCode::InvalidResponse, "WH-XB900N voice-guidance setting was not confirmed");
    }
}

} // namespace sony::protocol
