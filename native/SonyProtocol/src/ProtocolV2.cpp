#include "sony/protocol/ProtocolV2.h"
#include "ProtocolHelpers.h"
#include "sony/protocol/V2Layouts.h"
#include <algorithm>

namespace sony::protocol {

using detail::clampEqValue;
using detail::codecName;

namespace {

// Auto-power-off codes: 0=Off, 1=5min, 2=30min, 3=1h, 4=3h, 5=when-taken-off
const std::pair<uint8_t, uint8_t> APO_CODES[] = {
    { 0x11, 0x00 }, { 0x00, 0x00 }, { 0x01, 0x01 }, { 0x02, 0x02 }, { 0x03, 0x03 }, { 0x10, 0x00 }
};

int apoIndexFromCode(uint8_t c0, uint8_t c1) {
    for (int i = 0; i < 6; i++) {
        if (APO_CODES[i].first == c0 && APO_CODES[i].second == c1) {
            return i;
        }
    }
    return 0;
}

} // namespace

ProtocolV2::ProtocolV2(SonyProtocolSession& session, bool earbuds, bool ultEqualizer)
    : _session(session), _earbuds(earbuds), _ultEqualizer(ultEqualizer) {}

void ProtocolV2::initDevice() {
    // V2 handshake init: 0x00 0x00 -> RET 0x01
    try {
        _session.sendAndAwaitResponse(
            SonyFrame{ .type = DataType::DataMdr, .payload = {0x00, 0x00} },
            0x01,
            -1,
            std::chrono::milliseconds(1000)
        );
    } catch (const SonyException&) {}
}

BatteryState ProtocolV2::getBattery() {
    // V2 battery request uses opcode 0x22
    BatteryState state;

    // Earbuds answer the single-battery query too (a WF-1000XM6 reports one level there),
    // which would hide left, right and case, so they skip it.
    if (!_earbuds) {
        // 1. Single battery (over-ear / main): GET 22 00 -> RET 23 00 <level> <charging>
        try {
            auto resp = _session.sendAndAwaitResponse(
                SonyFrame{ .type = DataType::DataMdr, .payload = {0x22, 0x00} },
                0x23,
                0x00,
                std::chrono::milliseconds(1000)
            );
            if (resp.payload.size() >= 4) {
                state.main = static_cast<int>(resp.payload[2]);
                state.charging = (resp.payload[3] == 1);
                return state;
            }
        } catch (const SonyException&) {}
    }

    // 2. Dual L/R battery for TWS earbuds: GET 22 09 -> RET 23 09 <Llvl> <Lchg> <Rlvl> <Rchg>
    try {
        auto resp = _session.sendAndAwaitResponse(
            SonyFrame{ .type = DataType::DataMdr, .payload = {0x22, 0x09} },
            0x23,
            0x09,
            std::chrono::milliseconds(1000)
        );
        if (resp.payload.size() >= 6) {
            state.left = static_cast<int>(resp.payload[2]);
            state.right = static_cast<int>(resp.payload[4]);
            state.main = std::min(*state.left, *state.right);
            state.charging = (resp.payload[3] == 1 || resp.payload[5] == 1);
        }
    } catch (const SonyException&) {}

    // 3. Case battery for TWS earbuds: GET 22 0a -> RET 23 0a <level> <chg>
    try {
        auto resp = _session.sendAndAwaitResponse(
            SonyFrame{ .type = DataType::DataMdr, .payload = {0x22, 0x0a} },
            0x23,
            0x0a,
            std::chrono::milliseconds(1000)
        );
        if (resp.payload.size() >= 4) {
            state.caseBattery = static_cast<int>(resp.payload[2]);
        }
    } catch (const SonyException&) {}

    return state;
}

NoiseControlState ProtocolV2::getNoiseControl() {
    // Newer earbuds (WF-1000XM6) answer type 0x19 and zero-fill 0x17, so 0x19 goes first.
    if (_ncAsmType != 0x17) {
        try {
            auto seamless = _session.sendAndAwaitResponse(
                SonyFrame{ .type = DataType::DataMdr, .payload = {0x66, kNcAsmSeamless} },
                0x67,
                kNcAsmSeamless,
                std::chrono::milliseconds(1000)
            );
            NoiseControlState state;
            if (parseNcAsmSeamless(seamless.payload, state)) {
                _ncAsmType = kNcAsmSeamless;
                if (state.mode == NoiseControlMode::Ambient && state.ambientLevel > 0) {
                    _lastAmbientLevel = state.ambientLevel;
                }
                return state;
            }
        } catch (const SonyException&) {
            if (_ncAsmType == kNcAsmSeamless) {
                throw;
            }
        }
        if (_ncAsmType == kNcAsmSeamless) {
            throw SonyException(SonyErrorCode::InvalidResponse, "Malformed noise-control response");
        }
    }

    // GET: 66 17 -> RET: 67 17 01 <effect> <settingType 0=NC/1=Ambient> <voice> <level>
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0x66, 0x17} },
        0x67,
        -1,
        std::chrono::milliseconds(1000)
    );

    if (resp.payload.size() < 7 || resp.payload[1] != 0x17 || resp.payload[2] != 1)
        throw SonyException(SonyErrorCode::InvalidResponse, "Malformed noise-control response");
    _ncAsmType = 0x17;
    NoiseControlState state;
    if (resp.payload.size() >= 7) {
        bool on = resp.payload[3] != 0;
        bool ambient = resp.payload[4] != 0;
        bool voice = resp.payload[5] != 0;
        int level = static_cast<int>(resp.payload[6]);

        if (!on) {
            state.mode = NoiseControlMode::Off;
        } else if (ambient) {
            state.mode = NoiseControlMode::Ambient;
        } else {
            state.mode = NoiseControlMode::NoiseCancelling;
        }
        state.ambientLevel = ambient ? level : 0;
        state.focusOnVoice = voice;
    }
    return state;
}

void ProtocolV2::setNoiseControl(const NoiseControlState& state) {
    if (_ncAsmType == kNcAsmSeamless) {
        // SET 68 19 01 <effect> <mode 0 NC / 1 ambient> <voice> <level> <auto> <sensitivity>
        if (state.mode == NoiseControlMode::Ambient && state.ambientLevel > 0) {
            _lastAmbientLevel = state.ambientLevel;
        }
        std::vector<uint8_t> seamless = {
            0x68,
            kNcAsmSeamless,
            0x01,
            static_cast<uint8_t>(state.mode == NoiseControlMode::Off ? 0 : 1),
            static_cast<uint8_t>(state.mode == NoiseControlMode::Ambient ? 1 : 0),
            static_cast<uint8_t>(state.focusOnVoice && state.mode == NoiseControlMode::Ambient ? 1 : 0),
            static_cast<uint8_t>(_lastAmbientLevel),
            0x00,
            0x00
        };
        _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(seamless) });
        return;
    }

    uint8_t effect = (state.mode == NoiseControlMode::Off) ? 0 : 1;
    uint8_t settingType = (state.mode == NoiseControlMode::Ambient) ? 1 : 0;
    uint8_t voice = state.focusOnVoice ? 1 : 0;
    uint8_t level = static_cast<uint8_t>(state.ambientLevel > 0 ? state.ambientLevel : 1);

    std::vector<uint8_t> payload = {
        0x68,
        0x17,
        0x01,
        effect,
        settingType,
        voice,
        level
    };

    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

uint8_t ProtocolV2::eqInquiredType() const noexcept {
    return _ultEqualizer ? kEqInquiredPresetAndUltMode : kEqInquiredPreset;
}

EqualizerState ProtocolV2::getEqualizer() {
    // GET: 56 00 -> RET: 57 00 <preset> 06 <bass+10> <b1..b5 +10>
    // ULT:  56 03 -> RET: 57 03 <preset> <ult> 06 <bass+10> <b1..b5 +10>
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0x56, eqInquiredType()} },
        0x57,
        -1,
        std::chrono::milliseconds(1000)
    );

    // Five-band (count 06) and ten-band (count 0a, WF-1000XM6) layouts both parse.
    EqualizerState state;
    if (!parseEqualizer(resp.payload, state))
        throw SonyException(SonyErrorCode::InvalidResponse, "Incomplete equalizer response");
    return state;
}

void ProtocolV2::setEqualizerPreset(int preset, uint8_t ultMode) {
    // SET preset: 58 00 <preset> 00
    // ULT:        58 03 <preset> <ult> 00
    std::vector<uint8_t> payload = { 0x58, eqInquiredType(), static_cast<uint8_t>(preset) };
    if (_ultEqualizer) {
        payload.push_back(ultMode);
    }
    payload.push_back(0x00);
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

void ProtocolV2::setEqualizerCustom(int clearBass, const std::array<int, 5>& bands, uint8_t ultMode) {
    // SET custom: 58 00 A0 06 <clearBass+10> <b1..b5 +10>
    // ULT:        58 03 A0 <ult> 06 <clearBass+10> <b1..b5 +10>
    std::vector<uint8_t> payload = { 0x58, eqInquiredType(), 0xa0 };
    if (_ultEqualizer) {
        payload.push_back(ultMode);
    }
    payload.push_back(0x06);
    payload.push_back(clampEqValue(clearBass));
    for (int b : bands) {
        payload.push_back(clampEqValue(b));
    }
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

bool ProtocolV2::getDsee() {
    // GET: e6 01 -> RET: e7 01 <enabled 0/1>
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0xe6, 0x01} },
        0xe7,
        0x01,
        std::chrono::milliseconds(1000)
    );
    if (resp.payload.size() >= 3) {
        return resp.payload[2] != 0;
    }
    throw SonyException(SonyErrorCode::InvalidResponse, "Incomplete Dsee response");
}

void ProtocolV2::powerOff() {
    // SET: 24 03 01 (POWER_SET_STATUS, POWER_OFF, USER_POWER_OFF)
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = {0x24, 0x03, 0x01} });
}

void ProtocolV2::setDsee(bool enabled) {
    // SET: e8 01 <enabled 0/1>
    std::vector<uint8_t> payload = {
        0xe8,
        0x01,
        static_cast<uint8_t>(enabled ? 0x01 : 0x00)
    };
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

std::string ProtocolV2::getFirmwareVersion() {
    // GET: 04 02 -> RET: 05 02 <ascii version...>
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0x04, 0x02} },
        0x05,
        -1,
        std::chrono::milliseconds(1000)
    );
    if (resp.payload.size() > 3) {
        return std::string(resp.payload.begin() + 3, resp.payload.end());
    }
    return "";
}

std::string ProtocolV2::getCodec() {
    // GET: 12 02 -> RET: 13 02 <codec>
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0x12, 0x02} },
        0x13,
        -1,
        std::chrono::milliseconds(1000)
    );
    if (resp.payload.size() >= 3) {
        return codecName(resp.payload[2]);
    }
    return "";
}

int ProtocolV2::getAutoPowerOff() {
    // GET: 26 05 -> RET: 27 05 <c0> <c1>
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0x26, 0x05} },
        0x27,
        -1,
        std::chrono::milliseconds(1000)
    );
    if (resp.payload.size() >= 4) {
        return apoIndexFromCode(resp.payload[2], resp.payload[3]);
    }
    throw SonyException(SonyErrorCode::InvalidResponse, "Incomplete AutoPowerOff response");
}

void ProtocolV2::setAutoPowerOff(int index) {
    if (index < 0 || index >= 6) {
        return;
    }
    // SET: 28 05 <c0> <c1>
    std::vector<uint8_t> payload = {
        0x28,
        0x05,
        APO_CODES[index].first,
        APO_CODES[index].second
    };
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

bool ProtocolV2::getSpeakToChat() {
    // GET: f6 0c -> RET: f7 0c <inverted_enabled>
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0xf6, 0x0c} },
        0xf7,
        0x0c,
        std::chrono::milliseconds(1000)
    );
    if (resp.payload.size() >= 3) {
        return resp.payload[2] == 0;
    }
    throw SonyException(SonyErrorCode::InvalidResponse, "Incomplete SpeakToChat response");
}

void ProtocolV2::setSpeakToChat(bool enabled) {
    // SET: f8 0c <inverted_enabled> 01
    std::vector<uint8_t> payload = {
        0xf8,
        0x0c,
        static_cast<uint8_t>(enabled ? 0x00 : 0x01),
        0x01
    };
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

bool ProtocolV2::getAdaptiveVolume() {
    // GET: f6 0a -> RET: f7 0a <inverted_enabled>
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdr, .payload = {0xf6, 0x0a} },
        0xf7,
        0x0a,
        std::chrono::milliseconds(1000)
    );
    if (resp.payload.size() >= 3) {
        return resp.payload[2] == 0;
    }
    throw SonyException(SonyErrorCode::InvalidResponse, "Incomplete AdaptiveVolume response");
}

void ProtocolV2::setAdaptiveVolume(bool enabled) {
    // SET: f8 0a <inverted_enabled>
    std::vector<uint8_t> payload = {
        0xf8,
        0x0a,
        static_cast<uint8_t>(enabled ? 0x00 : 0x01)
    };
    _session.send(SonyFrame{ .type = DataType::DataMdr, .payload = std::move(payload) });
}

// =========================================================================
// PLAYBACK (MULTIPOINT)
// =========================================================================

std::vector<PlaybackDevice> ProtocolV2::getPlaybackDevices() {
    // Which list layout, if any: T2 GET 06 00 -> RET 07 00 <functions>, read once per link
    if (!_pairedDevicesType) {
        auto resp = _session.sendAndAwaitResponse(
            SonyFrame{ .type = DataType::DataMdrNo2, .payload = {0x06, 0x00} },
            0x07,
            0x00,
            std::chrono::milliseconds(1000)
        );
        const auto functions = parseSupportFunctions(resp.payload).value_or(std::vector<uint8_t>{});
        const auto has = [&](uint8_t function) { return std::ranges::find(functions, function) != functions.end(); };
        if (!has(kT2SourceSwitch)) {
            _pairedDevicesType = 0xff;
        } else if (has(kT2PairedDevicesWithClass) || has(kT2PairedDevicesWithClassLe)) {
            _pairedDevicesType = kPeripheralPairedDevicesWithClass;
        } else {
            _pairedDevicesType = kPeripheralPairedDevices;
        }
    }
    if (*_pairedDevicesType == 0xff) {
        throw SonyException(SonyErrorCode::Unsupported, "Headset doesn't support source switching");
    }

    // T2 GET 36 <type> -> RET 37 <type> <devices> <playing slot>
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdrNo2, .payload = {0x36, *_pairedDevicesType} },
        0x37,
        *_pairedDevicesType,
        std::chrono::milliseconds(1000)
    );
    auto devices = parsePlaybackDevices(resp.payload);
    if (!devices) {
        throw SonyException(SonyErrorCode::InvalidResponse, "Malformed paired-device list");
    }
    return *devices;
}

void ProtocolV2::switchPlayback(const std::string& address) {
    // T2 SET 3c 01 <address> -> NTFY 3d 01 <result> <address>. The headset hands the audio
    // over first, so the answer can take a few seconds.
    std::vector<uint8_t> payload = {0x3c, kPeripheralSourceSwitch};
    payload.insert(payload.end(), address.begin(), address.end());
    auto resp = _session.sendAndAwaitResponse(
        SonyFrame{ .type = DataType::DataMdrNo2, .payload = std::move(payload) },
        0x3d,
        kPeripheralSourceSwitch,
        std::chrono::milliseconds(5000)
    );
    const auto result = parsePlaybackSwitch(resp.payload);
    if (!result || !result->succeeded) {
        throw SonyException(SonyErrorCode::InvalidResponse, "Headset refused to switch playback");
    }
}

} // namespace sony::protocol
