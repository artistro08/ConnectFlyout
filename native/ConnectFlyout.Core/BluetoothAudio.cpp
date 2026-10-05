#include "pch.h"

#include "BluetoothAudio.h"
#include <bluetoothapis.h>
#include <winioctl.h>
#include <bthioctl.h>

#pragma comment(lib, "Bthprops.lib")

#include "sony/transport/Logger.h"

#include <Windows.h>
#include <ks.h>
#include <ksmedia.h>
#include <mmdeviceapi.h>
#include <devicetopology.h>
#include <functiondiscoverykeys_devpkey.h>

#include <winrt/Windows.Devices.Bluetooth.h>
#include <winrt/Windows.Devices.Enumeration.h>

#include <optional>
#include <set>
#include <string>

namespace sony::audio {

namespace {

constexpr std::string_view kCategory = "sony.audio";

// Bluetooth audio property set and its one-shot requests. Not in the public SDK headers; the
// values come from the Windows driver kit (bthhfpddi.h) and are what Settings > Sound uses.
constexpr GUID kPropSetBtAudio = {0x7fa06c40, 0xb8f6, 0x4c7e, {0x85, 0x56, 0xe8, 0xc3, 0x3a, 0x12, 0xe5, 0x4d}};
constexpr ULONG kOneShotReconnect = 0;

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

} // namespace

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
