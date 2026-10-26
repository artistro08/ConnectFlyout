#pragma once

#include <algorithm>
#include <cstdint>
#include <string>

// Byte-level helpers shared by the V1 and V2 command sets. Both generations
// encode equalizer values as value+10 and report the same codec codes.
namespace sony::protocol::detail {

inline std::string codecName(uint8_t code) {
    switch (code) {
        case 0x01: return "SBC";
        case 0x02: return "AAC";
        case 0x10: return "LDAC";
        case 0x20: return "aptX";
        case 0x21: return "aptX HD";
        default:   return "";
    }
}

// Sets the codec from a codec byte. A codec this app doesn't know leaves the one already
// shown alone and returns false, so the screen never blanks.
inline bool applyCodec(uint8_t code, std::string& codec) {
    std::string name = codecName(code);
    if (name.empty()) {
        return false;
    }
    codec = std::move(name);
    return true;
}

inline uint8_t clampEqValue(int v) {
    return static_cast<uint8_t>(std::max(-10, std::min(10, v)) + 10);
}

} // namespace sony::protocol::detail
