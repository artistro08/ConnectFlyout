#include "pch.h"

#include "BluetoothAudio.h"
#include <bluetoothapis.h>
#include <winioctl.h>
#include <bthioctl.h>

#pragma comment(lib, "Bthprops.lib")

#include "sony/transport/Logger.h"

#include <Windows.h>
#include <audioclient.h>
#include <ks.h>
#include <ksmedia.h>
#include <mmdeviceapi.h>
#include <devicetopology.h>
#include <functiondiscoverykeys_devpkey.h>

#include <winrt/Windows.Devices.Bluetooth.h>
#include <winrt/Windows.Devices.Enumeration.h>

#include <algorithm>
#include <chrono>
#include <condition_variable>
#include <cstring>
#include <fstream>
#include <memory>
#include <mutex>
#include <thread>
#include <iterator>
#include <optional>
#include <set>
#include <string>
#include <vector>

namespace sony::audio {

namespace {

constexpr std::string_view kCategory = "sony.audio";

// Bluetooth audio property set and its one-shot requests. Not in the public SDK headers; the
// values come from the Windows driver kit (bthhfpddi.h) and are what Settings > Sound uses.
constexpr GUID kPropSetBtAudio = {0x7fa06c40, 0xb8f6, 0x4c7e, {0x85, 0x56, 0xe8, 0xc3, 0x3a, 0x12, 0xe5, 0x4d}};
constexpr ULONG kOneShotReconnect = 0;

// PKEY_AudioEndpoint_FormFactor: mmdeviceapi.h only declares it and no import library defines it
constexpr PROPERTYKEY kFormFactorKey = {{0x1da5d803, 0xd492, 0x4edd, {0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e}}, 0};

// The headset's container ID: every Windows device node it owns (Bluetooth, audio
// endpoints, hands-free) shares it
std::optional<winrt::guid> containerIdOf(uint64_t bluetoothAddress) {
    using namespace winrt::Windows::Devices;

    const auto device = Bluetooth::BluetoothDevice::FromBluetoothAddressAsync(bluetoothAddress).get();
    if (!device) {
        Logger::warn(kCategory, "Windows doesn't know a Bluetooth device at that address");
        return std::nullopt;
    }

    // A paired Bluetooth device is an association endpoint, which keeps its container under
    // the Aep key; the plain key covers anything else
    const auto information = Enumeration::DeviceInformation::CreateFromIdAsync(
        device.DeviceId(),
        {L"System.Devices.Aep.ContainerId", L"System.Devices.ContainerId"},
        Enumeration::DeviceInformationKind::AssociationEndpoint).get();
    for (const auto* key : {L"System.Devices.Aep.ContainerId", L"System.Devices.ContainerId"}) {
        if (const auto value = information.Properties().TryLookup(key)) {
            if (const auto container = value.try_as<winrt::Windows::Foundation::IReference<winrt::guid>>()) {
                return container.Value();
            }
        }
    }
    return std::nullopt;
}

// The KS filter device behind an audio endpoint (endpoint -> connector -> the part it
// connects to -> that part's topology), which is what takes the Bluetooth request
std::optional<std::wstring> filterIdOf(IMMDevice* endpoint) {
    winrt::com_ptr<IDeviceTopology> topology;
    winrt::com_ptr<IConnector> connector;
    winrt::com_ptr<IConnector> connectedTo;
    if (FAILED(endpoint->Activate(__uuidof(IDeviceTopology), CLSCTX_ALL, nullptr, topology.put_void())) ||
        FAILED(topology->GetConnector(0, connector.put())) ||
        FAILED(connector->GetConnectedTo(connectedTo.put()))) {
        return std::nullopt;
    }

    const auto part = connectedTo.try_as<IPart>();
    winrt::com_ptr<IDeviceTopology> filterTopology;
    LPWSTR filterId = nullptr;
    if (!part || FAILED(part->GetTopologyObject(filterTopology.put())) || FAILED(filterTopology->GetDeviceId(&filterId))) {
        return std::nullopt;
    }
    std::wstring result(filterId);
    CoTaskMemFree(filterId);
    return result;
}

bool sameContainer(IMMDevice* endpoint, const winrt::guid& containerId) {
    winrt::com_ptr<IPropertyStore> properties;
    if (FAILED(endpoint->OpenPropertyStore(STGM_READ, properties.put()))) {
        return false;
    }
    PROPVARIANT value;
    PropVariantInit(&value);
    const bool same = SUCCEEDED(properties->GetValue(PKEY_Device_ContainerId, &value)) &&
        value.vt == VT_CLSID && value.puuid != nullptr &&
        *value.puuid == static_cast<GUID>(containerId);
    PropVariantClear(&value);
    return same;
}

// A 16-bit PCM .wav's format and samples, read by walking the RIFF chunks for "fmt " and "data"
struct WavClip {
    WAVEFORMATEX format{};
    std::vector<uint8_t> samples;
};

std::shared_ptr<const WavClip> readWav(const std::wstring& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) {
        Logger::warn(kCategory, "Couldn't open the sound file " + winrt::to_string(path));
        return nullptr;
    }
    const std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    if (bytes.size() < 12 || std::memcmp(bytes.data(), "RIFF", 4) != 0 || std::memcmp(bytes.data() + 8, "WAVE", 4) != 0) {
        Logger::warn(kCategory, "The sound file isn't a .wav");
        return nullptr;
    }

    WavClip clip;
    bool haveFormat = false;
    size_t offset = 12;
    while (offset + 8 <= bytes.size()) {
        uint32_t size = 0;
        std::memcpy(&size, bytes.data() + offset + 4, sizeof(size));
        if (offset + 8 + size > bytes.size()) {
            break;
        }
        const uint8_t* body = bytes.data() + offset + 8;
        if (std::memcmp(bytes.data() + offset, "fmt ", 4) == 0 && size >= 16) {
            std::memcpy(&clip.format, body, 16);
            clip.format.cbSize = 0;
            haveFormat = true;
        } else if (std::memcmp(bytes.data() + offset, "data", 4) == 0) {
            clip.samples.assign(body, body + size);
        }
        offset += 8 + size + (size & 1);
    }

    // 16-bit only: zero bytes are silence there, which the lead-in and keep-awake rely on
    if (!haveFormat || clip.format.wFormatTag != WAVE_FORMAT_PCM || clip.format.wBitsPerSample != 16 || clip.format.nBlockAlign == 0) {
        Logger::warn(kCategory, "The sound file isn't 16-bit PCM");
        return nullptr;
    }
    clip.samples.resize(clip.samples.size() / clip.format.nBlockAlign * clip.format.nBlockAlign);  // whole frames only
    if (clip.samples.empty()) {
        Logger::warn(kCategory, "The sound file has no samples");
        return nullptr;
    }
    return std::make_shared<const WavClip>(std::move(clip));
}

// The headset's stereo output. Its hands-free endpoint shares the container but reports the
// Headset form factor.
winrt::com_ptr<IMMDevice> stereoOutputOf(const winrt::guid& containerId) {
    const auto enumerator = winrt::create_instance<IMMDeviceEnumerator>(__uuidof(MMDeviceEnumerator));
    winrt::com_ptr<IMMDeviceCollection> endpoints;
    winrt::check_hresult(enumerator->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, endpoints.put()));
    UINT count = 0;
    winrt::check_hresult(endpoints->GetCount(&count));

    for (UINT i = 0; i < count; ++i) {
        winrt::com_ptr<IMMDevice> endpoint;
        if (FAILED(endpoints->Item(i, endpoint.put())) || !sameContainer(endpoint.get(), containerId)) {
            continue;
        }
        winrt::com_ptr<IPropertyStore> properties;
        if (FAILED(endpoint->OpenPropertyStore(STGM_READ, properties.put()))) {
            continue;
        }
        PROPVARIANT value;
        PropVariantInit(&value);
        const bool handsFree = SUCCEEDED(properties->GetValue(kFormFactorKey, &value)) &&
            value.vt == VT_UI4 && value.ulVal == Headset;
        PropVariantClear(&value);
        if (!handsFree) {
            return endpoint;
        }
    }
    return nullptr;
}

// Keep Awake: one render stream per process to the headset's stereo output. Bluetooth
// headphones drop the first second or so of audio after their link has gone idle, so the stream
// stays open after each sound, fed with near-silence, for kKeepAwakeMs (NVDA's default). A sound
// that opens the stream cold gets kColdLeadInMs of silence in front, so its audible part lands
// after the link has woken up. The near-silence is zeros with a one-LSB blip kBlipsPerSecond
// times a second (Sound Keeper's "Fluctuate"): some devices treat pure zeros as nothing playing.
constexpr ULONGLONG kKeepAwakeMs = 30'000;
constexpr uint32_t kColdLeadInMs = 750;
constexpr uint32_t kBlipsPerSecond = 50;
constexpr REFERENCE_TIME kBufferDuration = 2'000'000; // 200 ms

struct Stream {
    uint64_t bluetoothAddress = 0;
    winrt::com_ptr<IAudioClient> client;
    winrt::com_ptr<IAudioRenderClient> render;
    WAVEFORMATEX format{};
    UINT32 bufferFrames = 0;
    std::vector<uint8_t> queue;   // bytes still to play, lead-in included
    ULONGLONG lastSoundTick = 0;  // when the queue last had sound in it
    bool closing = false;
};

std::mutex gMutex;                      // guards gStream, gClip and gClipPath
std::condition_variable gWake;
std::shared_ptr<Stream> gStream;
std::shared_ptr<const WavClip> gClip;
std::wstring gClipPath;

// Queues the clip on the stream; the caller holds gMutex
void enqueue(Stream& stream, const WavClip& clip) {
    stream.queue.insert(stream.queue.end(), clip.samples.begin(), clip.samples.end());
    stream.lastSoundTick = GetTickCount64();
    gWake.notify_all();
}

// Fills frames with near-silence: zeros, plus a one-LSB blip on the first channel every so often
void fillNearSilence(BYTE* buffer, UINT32 frames, const WAVEFORMATEX& format, UINT32& frameCounter) {
    std::memset(buffer, 0, static_cast<size_t>(frames) * format.nBlockAlign);
    const UINT32 blipEvery = std::max<UINT32>(1, format.nSamplesPerSec / kBlipsPerSecond);
    for (UINT32 f = 0; f < frames; ++f, ++frameCounter) {
        if (frameCounter % blipEvery == 0) {
            buffer[static_cast<size_t>(f) * format.nBlockAlign] = 1;  // little-endian PCM: the low byte of channel 0
        }
    }
}

// The feeder: tops the buffer up every 10 ms from the queue, then with near-silence, and closes
// the stream once it has been quiet for kKeepAwakeMs or the device stops answering. Holds the
// module lock, so the component can't be unloaded under it.
void feedStream(std::shared_ptr<Stream> stream) {
    ++winrt::get_module_lock();
    winrt::init_apartment(winrt::apartment_type::multi_threaded);
    UINT32 frameCounter = 0;
    {
        std::unique_lock lock(gMutex);
        while (!stream->closing) {
            UINT32 padding = 0;
            if (FAILED(stream->client->GetCurrentPadding(&padding))) {
                Logger::info(kCategory, "The headset's output went away, closing the keep-awake stream");
                break;
            }
            const UINT32 frames = padding < stream->bufferFrames ? stream->bufferFrames - padding : 0;
            if (frames > 0) {
                BYTE* buffer = nullptr;
                if (FAILED(stream->render->GetBuffer(frames, &buffer))) {
                    break;
                }
                const size_t frameBytes = stream->format.nBlockAlign;
                const size_t wanted = static_cast<size_t>(frames) * frameBytes;
                const size_t fromQueue = std::min(stream->queue.size(), wanted) / frameBytes * frameBytes;
                std::memcpy(buffer, stream->queue.data(), fromQueue);
                stream->queue.erase(stream->queue.begin(), stream->queue.begin() + static_cast<std::ptrdiff_t>(fromQueue));
                fillNearSilence(buffer + fromQueue, static_cast<UINT32>((wanted - fromQueue) / frameBytes), stream->format, frameCounter);
                stream->render->ReleaseBuffer(frames, 0);
                if (fromQueue > 0) {
                    stream->lastSoundTick = GetTickCount64();
                }
            }
            if (stream->queue.empty() && GetTickCount64() - stream->lastSoundTick > kKeepAwakeMs) {
                Logger::debug(kCategory, "Quiet for a while, closing the keep-awake stream");
                break;
            }
            gWake.wait_for(lock, std::chrono::milliseconds(10));
        }
        stream->closing = true;
        if (stream->client) {
            stream->client->Stop();
        }
        stream->render = nullptr;
        stream->client = nullptr;
        if (gStream == stream) {
            gStream.reset();
        }
    }
    winrt::uninit_apartment();
    --winrt::get_module_lock();
}

// Opens the stream on the endpoint in the clip's format; the engine converts to the mix format
std::shared_ptr<Stream> openStream(uint64_t bluetoothAddress, IMMDevice* endpoint, const WAVEFORMATEX& format) {
    auto stream = std::make_shared<Stream>();
    stream->bluetoothAddress = bluetoothAddress;
    stream->format = format;
    winrt::check_hresult(endpoint->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, stream->client.put_void()));
    winrt::check_hresult(stream->client->Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
        kBufferDuration, 0, &format, nullptr));
    winrt::check_hresult(stream->client->GetBufferSize(&stream->bufferFrames));
    winrt::check_hresult(stream->client->GetService(__uuidof(IAudioRenderClient), stream->render.put_void()));
    winrt::check_hresult(stream->client->Start());
    stream->lastSoundTick = GetTickCount64();
    return stream;
}

} // namespace

bool playToHeadset(uint64_t bluetoothAddress, const std::wstring& path) {
    std::unique_lock lock(gMutex);
    if (!gClip || gClipPath != path) {
        gClip = readWav(path);
        gClipPath = path;
        if (!gClip) {
            return false;
        }
    }
    const auto clip = gClip;

    // Warm: the stream is still open on this headset, so skip the Windows lookups
    if (gStream && !gStream->closing && gStream->bluetoothAddress == bluetoothAddress) {
        enqueue(*gStream, *clip);
        return true;
    }
    lock.unlock();

    try {
        const auto containerId = containerIdOf(bluetoothAddress);
        if (!containerId) {
            Logger::warn(kCategory, "Couldn't find the headset's container to play a sound on it");
            return false;
        }
        const auto endpoint = stereoOutputOf(*containerId);
        if (!endpoint) {
            Logger::info(kCategory, "The headset has no active stereo output, so no sound was played");
            return false;
        }

        lock.lock();
        if (gStream) {
            gStream->closing = true;
            gWake.notify_all();
        }
        auto stream = openStream(bluetoothAddress, endpoint.get(), clip->format);
        gStream = stream;
        // Cold start: the link is waking up, so lead with silence
        stream->queue.assign(static_cast<size_t>(clip->format.nSamplesPerSec) * kColdLeadInMs / 1000 * clip->format.nBlockAlign, 0);
        std::thread(feedStream, stream).detach();
        Logger::debug(kCategory, "Opened the keep-awake stream on the headset's output");
        enqueue(*stream, *clip);
        return true;
    } catch (const winrt::hresult_error& error) {
        Logger::warn(kCategory, "Couldn't play a sound on the headset: " + winrt::to_string(error.message()));
        return false;
    }
}

bool requestAudioConnect(uint64_t bluetoothAddress) {
    const auto containerId = containerIdOf(bluetoothAddress);
    if (!containerId) {
        Logger::warn(kCategory, "Couldn't find the headset's container to connect its audio");
        return false;
    }

    const auto enumerator = winrt::create_instance<IMMDeviceEnumerator>(__uuidof(MMDeviceEnumerator));
    winrt::com_ptr<IMMDeviceCollection> endpoints;
    winrt::check_hresult(enumerator->EnumAudioEndpoints(
        eAll, DEVICE_STATE_ACTIVE | DEVICE_STATE_UNPLUGGED | DEVICE_STATE_NOTPRESENT, endpoints.put()));
    UINT count = 0;
    winrt::check_hresult(endpoints->GetCount(&count));

    // One request per filter: the headset's stereo and hands-free endpoints can share one
    std::set<std::wstring> asked;
    bool accepted = false;
    for (UINT i = 0; i < count; ++i) {
        winrt::com_ptr<IMMDevice> endpoint;
        if (FAILED(endpoints->Item(i, endpoint.put())) || !sameContainer(endpoint.get(), *containerId)) {
            continue;
        }
        const auto filterId = filterIdOf(endpoint.get());
        if (!filterId || !asked.insert(*filterId).second) {
            continue;
        }

        winrt::com_ptr<IMMDevice> filter;
        winrt::com_ptr<IKsControl> control;
        if (FAILED(enumerator->GetDevice(filterId->c_str(), filter.put())) ||
            FAILED(filter->Activate(__uuidof(IKsControl), CLSCTX_ALL, nullptr, control.put_void()))) {
            continue;
        }
        KSPROPERTY property{};
        property.Set = kPropSetBtAudio;
        property.Id = kOneShotReconnect;
        property.Flags = KSPROPERTY_TYPE_GET;
        ULONG returned = 0;
        if (SUCCEEDED(control->KsProperty(&property, sizeof(property), nullptr, 0, &returned))) {
            accepted = true;
        }
    }

    Logger::info(kCategory, accepted
        ? "Asked Windows to connect the headset's audio"
        : "No Bluetooth audio filter took the connect request (" + std::to_string(asked.size()) + " found)");
    return accepted;
}

bool requestAudioDisconnect(uint64_t bluetoothAddress) {
    BLUETOOTH_FIND_RADIO_PARAMS search{};
    search.dwSize = static_cast<DWORD>(sizeof(search));

    HANDLE radio = nullptr;
    const HBLUETOOTH_RADIO_FIND find = BluetoothFindFirstRadio(&search, &radio);
    if (!find) {
        Logger::warn(kCategory, "Couldn't find a Bluetooth radio for full-device disconnect");
        return false;
    }

    BTH_ADDR address = static_cast<BTH_ADDR>(bluetoothAddress);
    bool accepted = false;

    do {
        DWORD returned = 0;
        accepted = DeviceIoControl(
            radio,
            IOCTL_BTH_DISCONNECT_DEVICE,
            &address,
            static_cast<DWORD>(sizeof(address)),
            nullptr,
            0,
            &returned,
            nullptr) != FALSE;

        CloseHandle(radio);
        radio = nullptr;

        if (accepted) {
            break;
        }
    } while (BluetoothFindNextRadio(find, &radio));

    BluetoothFindRadioClose(find);

    Logger::info(
        kCategory,
        accepted
            ? "Asked Windows to disconnect the headset's full Bluetooth link"
            : "Windows rejected the full Bluetooth disconnect request");

    return accepted;
}
} // namespace sony::audio
