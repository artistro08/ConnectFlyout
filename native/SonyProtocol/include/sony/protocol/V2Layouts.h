#pragma once

#include "SemanticTypes.h"
#include "EqualizerPresets.h"

#include <cstdint>
#include <optional>
#include <span>
#include <string>
#include <vector>

namespace sony::protocol {

// Newer V2 layouts a real WF-1000XM6 uses that upstream's WH-1000XM5-era code doesn't
// know. Byte meanings from mos9527/SonyHeadphonesClient (ProtocolV2T1.hpp), confirmed
// against replies captured from a WF-1000XM6 on firmware 1.6.0.

inline constexpr uint8_t kNcAsmSeamless = 0x19;

// Noise control type 0x19, same layout for GET reply (0x67), SET (0x68) and notify (0x69):
//   <op> 19 <status: 01 final> <effect: 00 off, 01 on> <mode: 00 NC, 01 ambient>
//        <voice: 01 focus on voice> <ambient level 0-20> <auto ambient> <adaptive sensitivity>
// Returns false when the payload isn't that layout.
inline bool parseNcAsmSeamless(std::span<const uint8_t> payload, NoiseControlState& state) noexcept {
    if (payload.size() < 9 || payload[1] != kNcAsmSeamless) {
        return false;
    }
    const bool on = payload[3] != 0;
    const bool ambient = payload[4] == 1;

    if (!on) {
        state.mode = NoiseControlMode::Off;
    } else if (ambient) {
        state.mode = NoiseControlMode::Ambient;
    } else {
        state.mode = NoiseControlMode::NoiseCancelling;
    }
    state.ambientLevel = state.mode == NoiseControlMode::Ambient ? static_cast<int>(payload[6]) : 0;
    state.focusOnVoice = payload[5] == 1;
    return true;
}

// Equalizer inquired types. Most V2 devices use PresetEq (0x00); the ULT series only answers
// PresetEqAndUltMode (0x03), which inserts an ULT-mode byte between the preset and the bands
// (mos9527/SonyHeadphonesClient, EqEbbInquiredType). Confirmed from a ULT WEAR HCI capture.
inline constexpr uint8_t kEqInquiredPreset = 0x00;
inline constexpr uint8_t kEqInquiredPresetAndUltMode = 0x03;

// Equalizer reply or notify:
//   type 0x00: <op> 00 <preset> <band count> <bands...>
//   type 0x03: <op> 03 <preset> <ult mode> <band count> <bands...>
// Six bands are Clear Bass then 400 Hz-16 kHz, each dB + 10. Other counts (a ten-band
// WF-1000XM6 sends 10, each step + 6) keep only the preset; the five-band curve stays empty.
// Returns false when the payload isn't an equalizer message.
inline bool parseEqualizer(std::span<const uint8_t> payload, EqualizerState& state) noexcept {
    if (payload.size() < 4 ||
        (payload[1] != kEqInquiredPreset && payload[1] != kEqInquiredPresetAndUltMode)) {
        return false;
    }
    const size_t countAt = payload[1] == kEqInquiredPresetAndUltMode ? 4 : 3;
    if (payload.size() <= countAt) {
        return false;
    }
    state.preset = normalizeEqualizerPreset(static_cast<int>(payload[2]));
    if (payload[1] == kEqInquiredPresetAndUltMode) {
        state.ultMode = payload[3];
    }
    state.clearBass = 0;
    state.bands = {0, 0, 0, 0, 0};

    if (payload[countAt] == 6 && payload.size() >= countAt + 7) {
        state.clearBass = static_cast<int>(payload[countAt + 1]) - 10;
        for (size_t i = 0; i < state.bands.size(); ++i) {
            state.bands[i] = static_cast<int>(payload[countAt + 2 + i]) - 10;
        }
    }
    return true;
}

// =========================================================================
// TABLE 2: PERIPHERAL (MULTIPOINT) MESSAGES
// =========================================================================
//
// Sent on the second command table (DataMdrNo2). Layouts from mos9527/SonyHeadphonesClient
// (ProtocolV2T2.hpp, HeadphonesV2T2.cpp).

// Support functions (T2 06 00 -> 07 00 <count> {<function> <priority>}...)
inline constexpr uint8_t kT2PairedDevices = 0x30;
inline constexpr uint8_t kT2SourceSwitch = 0x31;
inline constexpr uint8_t kT2PairedDevicesWithClass = 0x32;
inline constexpr uint8_t kT2PairedDevicesWithClassLe = 0x33;

// Peripheral inquired types
inline constexpr uint8_t kPeripheralPairedDevices = 0x00;
inline constexpr uint8_t kPeripheralSourceSwitch = 0x01;
inline constexpr uint8_t kPeripheralPairedDevicesWithClass = 0x02;

// Support function list: <op 07> 00 <count> {<function> <priority>}...
// Returns the function codes, or nothing when the payload isn't that reply.
inline std::optional<std::vector<uint8_t>> parseSupportFunctions(std::span<const uint8_t> payload) {
    if (payload.size() < 3 || payload[0] != 0x07) {
        return std::nullopt;
    }
    const size_t count = payload[2];
    if (payload.size() < 3 + (count * 2)) {
        return std::nullopt;
    }
    std::vector<uint8_t> functions;
    for (size_t i = 0; i < count; ++i) {
        functions.push_back(payload[3 + (i * 2)]);
    }
    return functions;
}

// Paired-device list, reply (0x37) or notify (0x39):
//   <op> <type 00 | 02> <count> {<address: 17 ASCII> <slot> [<class of device: 3>] <name length> <name>}...
//   <slot playing>
// Slot 0 is paired but not connected; the device whose slot matches the last byte has
// playback. Returns the connected devices only, or nothing when the payload isn't that list.
inline std::optional<std::vector<PlaybackDevice>> parsePlaybackDevices(std::span<const uint8_t> payload) {
    if (payload.size() < 4 || (payload[0] != 0x37 && payload[0] != 0x39)) {
        return std::nullopt;
    }
    const uint8_t type = payload[1];
    if (type != kPeripheralPairedDevices && type != kPeripheralPairedDevicesWithClass) {
        return std::nullopt;
    }
    const size_t classBytes = type == kPeripheralPairedDevicesWithClass ? 3 : 0;

    struct Entry {
        PlaybackDevice device;
        uint8_t slot;
    };
    std::vector<Entry> entries;
    size_t offset = 3;
    for (size_t i = 0; i < payload[2]; ++i) {
        if (payload.size() < offset + 17 + 1 + classBytes + 1) {
            return std::nullopt;
        }
        Entry entry{};
        entry.device.address.assign(payload.begin() + offset, payload.begin() + offset + 17);
        offset += 17;
        entry.slot = payload[offset++];
        offset += classBytes;
        const size_t nameLength = payload[offset++];
        if (payload.size() < offset + nameLength) {
            return std::nullopt;
        }
        entry.device.name.assign(payload.begin() + offset, payload.begin() + offset + nameLength);
        offset += nameLength;
        entries.push_back(std::move(entry));
    }
    if (payload.size() < offset + 1) {
        return std::nullopt;
    }
    const uint8_t playingSlot = payload[offset];

    std::vector<PlaybackDevice> connected;
    for (auto& entry : entries) {
        if (entry.slot == 0) {
            continue;
        }
        entry.device.playing = entry.slot == playingSlot;
        connected.push_back(std::move(entry.device));
    }
    return connected;
}

// Source switch result, notify (0x3d): <op> 01 <result: 00 done, else refused> <address: 17 ASCII>
struct PlaybackSwitchResult {
    bool succeeded{false};
    std::string address;
};

inline std::optional<PlaybackSwitchResult> parsePlaybackSwitch(std::span<const uint8_t> payload) {
    if (payload.size() < 20 || payload[0] != 0x3d || payload[1] != kPeripheralSourceSwitch) {
        return std::nullopt;
    }
    return PlaybackSwitchResult{
        .succeeded = payload[2] == 0x00,
        .address = std::string(payload.begin() + 3, payload.begin() + 20),
    };
}

// Marks the device with this address as the one playing.
inline void markPlaying(std::vector<PlaybackDevice>& devices, const std::string& address) {
    for (auto& device : devices) {
        device.playing = device.address == address;
    }
}

} // namespace sony::protocol
