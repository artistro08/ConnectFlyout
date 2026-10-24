#pragma once

#include <cstdint>
#include <string>

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

// Plays a PCM .wav on the headset's stereo output only, whatever Windows' default device is.
// Renders through WASAPI, so Windows sees no media session. The hands-free endpoint is
// skipped: a stream on it would switch the headset into call mode. The stream stays open with
// near-silence for 30 s after each sound, so the headset's link doesn't go idle between
// presses; a cold start leads with 750 ms of silence while the link wakes. Returns once the
// clip is queued. False when the file, the headset or its output isn't usable.
[[nodiscard]] bool playToHeadset(uint64_t bluetoothAddress, const std::wstring& path);
} // namespace sony::audio
