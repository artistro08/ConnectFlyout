#pragma once

#include <cstdint>

namespace sony::audio {

// Asks Windows to connect a paired Bluetooth headset's audio, the same request Windows sends
// when you click Connect on it in Settings > Sound: KSPROPERTY_ONESHOT_RECONNECT on each of its
// Bluetooth audio filters. The headset is found by matching its container ID against the audio
// endpoints'.
//
// Returns true when at least one filter took the request. That only means Windows is trying;
// the headset still has to be on and in range. Blocks, so call it off the UI thread.
[[nodiscard]] bool requestAudioConnect(uint64_t bluetoothAddress);

// Asks Windows to disconnect the paired headset's full Bluetooth link.
// Used immediately before reconnecting so Windows performs a fresh A2DP negotiation.
[[nodiscard]] bool requestAudioDisconnect(uint64_t bluetoothAddress);
} // namespace sony::audio
